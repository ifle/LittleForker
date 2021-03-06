﻿using System;
using System.Threading.Tasks;
using LittleForker.Infra;
using LittleForker.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker
{
    public class ProcessSupervisorTests : IDisposable
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly IDisposable _logCapture;

        public ProcessSupervisorTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
            _logCapture = LogHelper.Capture(outputHelper, LogProvider.SetCurrentLogProvider);
            Environment.SetEnvironmentVariable(Constants.ProcessIdEnvironmentVariable, null);
        }

        [Fact]
        public void Given_invalid_process_path_then_state_should_be_StartError()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, "c:/", "invalid.exe");
            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.Exception.ShouldNotBeNull();

            _outputHelper.WriteLine(supervisor.Exception.ToString());
        }

        [Fact]
        public void Given_invalid_working_directory_then_state_should_be_StartError()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, "c:/does_not_exist", "git.exe");
            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.Exception.ShouldNotBeNull();

            _outputHelper.WriteLine(supervisor.Exception.ToString());
        }

        [Fact]
        public async Task Given_short_running_exe_then_should_run_to_exit()
        {
            var supervisor = new ProcessSupervisor(
                ProcessRunType.SelfTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./SelfTerminatingProcess/SelfTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var whenStateIsExited = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);

            supervisor.Start();
            await whenStateIsExited;

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Exception.ShouldBeNull();
            supervisor.ProcessInfo.ExitCode.ShouldBe(0);
        }

        [Fact]
        public async Task Given_long_running_exe_then_should_exit_when_stopped()
        {
            var supervisor = new ProcessSupervisor(
                ProcessRunType.NonTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./NonTerminatingProcess/NonTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2($"Process: {data}");
            var stateIsRunning = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await stateIsRunning;

            await supervisor.Stop(TimeSpan.FromSeconds(2));
            await stateIsStopped;

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Exception.ShouldBeNull();
            supervisor.ProcessInfo.ExitCode.ShouldBe(0);
        }
        
        [Fact]
        public async Task Can_restart_a_stopped_short_running_process()
        {
            var supervisor = new ProcessSupervisor(
                ProcessRunType.SelfTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./SelfTerminatingProcess/SelfTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await stateIsStopped;

            supervisor.Start();
            await stateIsStopped;
        }

        [Fact]
        public async Task Can_restart_a_stopped_long_running_process()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet", "./LongRunningProcess/LongRunningProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(TimeSpan.FromSeconds(2));
            await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

            // Restart
            stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(TimeSpan.FromSeconds(2));
            await stateIsStopped;
        }

        [Fact]
        public async Task When_stop_a_non_terminating_process_then_should_exit_successfully()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet", "./LongRunningProcess/LongRunningProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(); // No timeout so will just kill the process
            await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

            _outputHelper.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
        }

        [Fact]
        public void Can_attempt_to_restart_a_failed_short_running_process()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "invalid.exe");
            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.Exception.ShouldNotBeNull();

            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.Exception.ShouldNotBeNull();
        }

        [Fact]
        public void WriteDotGraph()
        {
            var processController = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "invalid.exe");
            _outputHelper.WriteLine(processController.GetDotGraph());
        }

        public void Dispose()
        {
            _logCapture?.Dispose();
        }
    }
}