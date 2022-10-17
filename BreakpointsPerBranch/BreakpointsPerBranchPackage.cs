global using System;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
global using Task = System.Threading.Tasks.Task;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EnvDTE;
using EnvDTE100;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;

namespace BreakpointsPerBranch
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [Guid(PackageGuids.BreakpointsPerBranchString)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class BreakpointsPerBranchPackage : ToolkitPackage
    {
        private FileSystemWatcher gitBranchTracker; // whenever the branch is changed, record the current branch name
        private Debugger5 debugger; // whenever the debugger is launched, export the active breakpoints into a temp file named after that current branch.
        private string currentBranchName;
        private string currentSolutionIdentifier;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var solution = await VS.Solutions.GetCurrentSolutionAsync();
            var gitRepositoryPath = Path.Combine(Path.GetDirectoryName(solution.FullPath), ".git");

            if (!Directory.Exists(gitRepositoryPath))
                return;

            this.debugger = (Debugger5)(await VS.GetServiceAsync<DTE, DTE2>()).Debugger;

            this.currentSolutionIdentifier = CreateMD5(solution.Name);
            this.currentBranchName = ReadGitHeadFile(Path.Combine(gitRepositoryPath, "HEAD"));
            this.gitBranchTracker = new FileSystemWatcher(gitRepositoryPath, "HEAD")
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false
            };

            // start listening for branch changes
            this.gitBranchTracker.Renamed += OnBranchSwitched;
            this.gitBranchTracker.EnableRaisingEvents = true;
            // start listening for debugging sessions
            VS.Events.DebuggerEvents.EnterRunMode += OnDebuggingStarted;
        }

        private async void OnBranchSwitched(object sender, FileSystemEventArgs e)
        {
            string newBranchName = ReadGitHeadFile(e.FullPath);
            try
            {
                var newBranchBreakpoints = GetSavedBreakpointFilename(currentSolutionIdentifier, newBranchName);
                var currentBranchBreakpoints = GetSavedBreakpointFilename(currentSolutionIdentifier, currentBranchName);

                if (newBranchName == currentBranchName
                    || !File.Exists(newBranchBreakpoints)
                    || (File.Exists(currentBranchBreakpoints) && AreBreakpointsIdentical(newBranchBreakpoints, currentBranchBreakpoints)))
                {
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                foreach (var breakpoint in debugger.Breakpoints.Cast<Breakpoint>())
                {
                    breakpoint.Delete();
                }
                debugger.ImportBreakpoints(newBranchBreakpoints);
            }
            finally
            {
                currentBranchName = newBranchName;
            }
        }

        /// <summary>
        /// At the start of every debugging session, record the current set of breakpoints.
        /// </summary>
        private void OnDebuggingStarted() =>
            debugger.ExportBreakpoints(GetSavedBreakpointFilename(currentSolutionIdentifier, currentBranchName));

        // extract "refs/heads/branch-name" from the HEAD file containing "ref: refs/heads/branch-name"
        private static string ReadGitHeadFile(string filepath) =>
            File.ReadAllText(filepath).Split(new[] { ":" }, 2, StringSplitOptions.None)[1].Trim();

        private static bool AreBreakpointsIdentical(string newBranchBreakpoints, string currentBranchBreakpoints) =>
            File.ReadAllText(newBranchBreakpoints) == File.ReadAllText(currentBranchBreakpoints);

        private static string GetSavedBreakpointFilename(string currentSolutionIdentifier, string currentBranchName) =>
            Path.Combine(Path.GetTempPath(), $"{nameof(BreakpointsPerBranch)}-{currentSolutionIdentifier}-{CreateMD5(currentBranchName)}.xml");

        private static string CreateMD5(string input)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

            var sb = new StringBuilder(32);
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            VS.Events.DebuggerEvents.EnterRunMode -= OnDebuggingStarted;

            if(this.gitBranchTracker != null)
            {
                this.gitBranchTracker.Created -= OnBranchSwitched;
                this.gitBranchTracker.EnableRaisingEvents = false;
                this.gitBranchTracker.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}