//
// Trimmer Framework for Unity
// https://sttz.ch/trimmer
//

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace sttz.Trimmer.Editor
{

/// <summary>
/// Base class for distributions.
/// </summary>
/// <remarks>
/// Distributions take the builds generated by one or more Build Profiles and
/// process them in different ways, e.g.
/// * <see cref="ItchDistro"/>: Upload builds to itch.io
/// * <see cref="MASDistro"/>: Process a mac build for the Mac App Store (no automatic upload)
/// * <see cref="SteamDistro"/>: Upload builds to Steam
/// * <see cref="UploadDistro"/>: Zip and Upload builds to a FTP server
/// * <see cref="ZipDistro"/>: Zip builds
/// 
/// There are also more generic distros:
/// * <see cref="ScriptDistro"/>: Call a script with the build path
/// * <see cref="MetaDistro"/>: Execute multiple distros
/// 
/// To create a distro, select the type you want from Create » Trimmer » Distro in the
/// Project window's Create menu.
/// 
/// Note that while a distro is running, reloading of scripts is locked, as the
/// assembly reload would abort the distribution.
/// </remarks>
public abstract class DistroBase : ScriptableObject
{
    [MenuItem("Assets/Create/Trimmer/Distributions:", false, 99)]
    [MenuItem("Assets/Create/Trimmer/Distributions:", true)]
    static bool Dummy() { return false; }

    /// <summary>
    /// Process the builds of these Build Profiles.
    /// </summary>
    [HideInInspector] public List<BuildProfile> builds;

    /// <summary>
    /// Wether the distribution will raise an error if it has no
    /// build targets.
    /// </summary>
    public virtual bool CanRunWithoutBuildTargets { get { return false; } }

    /// <summary>
    /// Structure used to represent a set of builds.
    /// </summary>
    public struct BuildPath
    {
        public BuildTarget target;
        public string path;

        public BuildPath(BuildTarget target, string path)
        {
            this.target = target;
            this.path = path;
        }
    }

    /// <summary>
    /// Wether the distribution is currently running.
    /// </summary>
    /// <remarks>
    /// While the distribution is running, script reloading is locked.
    /// Call <see cref="ForceCancel"/> or select it from the distribution's
    /// gear menu in case the distribution gets stuck.
    /// </remarks>
    public bool IsRunning {
        get {
            return _isRunning;
        }
        protected set {
            if (_isRunning == value)
                return;
            
            _isRunning = value;

            if (_isRunning) {
                EditorApplication.LockReloadAssemblies();
            } else {
                EditorApplication.UnlockReloadAssemblies();
            }
        }
    }
    bool _isRunning;

    /// <summary>
    /// Check wether there are existing builds for all build target in all linked Build Profiles.
    /// </summary>
    public virtual bool HasAllBuilds()
    {
        foreach (var profile in builds) {
            if (profile == null) continue;
            foreach (var target in profile.BuildTargets) {
                var path = profile.GetLastBuildPath(target);
                if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Build all linked Build Profiles.
    /// </summary>
    /// <returns></returns>
    [ContextMenu("Build")]
    public bool Build()
    {
        return BuildAndGetBuildPaths(true) != null;
    }

    /// <summary>
    /// Process the builds of the linked Build Profiles and build the
    /// targets where no build exists.
    /// </summary>
    [ContextMenu("Distribute")]
    public void Distribute()
    {
        RunCoroutine(DistributeCoroutine());
    }

    /// <summary>
    /// Process the builds of the linked Build Profiles and build the
    /// targets where no build exists.
    /// </summary>
    /// <param name="forceBuild">Force rebuilding all targets, even if a build exists</param>
    public void Distribute(bool forceBuild)
    {
        RunCoroutine(DistributeCoroutine(forceBuild));
    }

    /// <summary>
    /// Force cancel the distribution. Only call in case the distribution
    /// gets stuck, e.g. because of an exception.
    /// </summary>
    [ContextMenu("Force Cancel")]
    public void ForceCancel()
    {
        Cancel();
        IsRunning = false;
    }

    /// <summary>
    /// Cancel the distribution.
    /// </summary>
    public virtual void Cancel()
    {
        if (runningScripts != null) {
            foreach (var terminator in runningScripts.ToList()) {
                terminator(true);
            }
        }
    }

    /// <summary>
    /// Build all missing targets and return the paths for all build of all linked Build Profiles.
    /// </summary>
    /// <param name="forceBuild">Force rebuilding all targets, even if a build exists</param>
    public virtual IEnumerable<BuildPath> BuildAndGetBuildPaths(bool forceBuild = false)
    {
        var paths = new Dictionary<BuildTarget, string>();

        // Some Unity versions' (seen on 2018.2b11) ReorderableList can change
        // the list during the build and cause the foreach to raise an exception
        foreach (var profile in builds.ToArray()) {
            if (profile == null) continue;
            foreach (var target in profile.BuildTargets) {
                var path = profile.GetLastBuildPath(target);
                if (forceBuild || string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) {
                    var options = BuildManager.GetDefaultOptions(target);
                    var error = BuildManager.Build(profile, options);
                    if (!string.IsNullOrEmpty(error)) {
                        return null;
                    }
                    path = profile.GetLastBuildPath(target);
                }
                paths[target] = path;
            }
        }

        return paths.Select(p => new BuildPath(p.Key, p.Value));
    }

    /// <summary>
    /// Coroutine to run the distribution.
    /// </summary>
    /// <remarks>
    /// This is not a Unity coroutine but a custom editor coroutine.
    /// Use <see cref="DistroBase.RunCoroutine"/> to start it and
    /// <see cref="DistroBase.GetSubroutineResult"/> to get its result.
    /// </remarks>
    /// <param name="forceBuild">Force rebuilding all targets, even if a build exists</param>
    public virtual IEnumerator DistributeCoroutine(bool forceBuild = false)
    {
        if (IsRunning) {
            yield return false; yield break;
        }

        IsRunning = true;

        var paths = BuildAndGetBuildPaths(forceBuild);
        if (paths == null) {
            IsRunning = false;
            yield return false; yield break;
        }

        if (!CanRunWithoutBuildTargets && !paths.Any()) {
            Debug.LogError(name + ": No build paths for distribution");
            IsRunning = false;
            yield return false; yield break;
        }

        yield return DistributeCoroutine(paths, forceBuild);

        IsRunning = false;

        yield return GetSubroutineResult<bool>();
    }

    /// <summary>
    /// Subroutine to override in subclasses to do the actual processing.
    /// </summary>
    /// <param name="buildPaths">Build paths of the linked Build Profiles</param>
    /// <param name="forceBuild">Force rebuilding all targets, even if a build exists</param>
    protected abstract IEnumerator DistributeCoroutine(IEnumerable<BuildPath> buildPaths, bool forceBuild);

    // -------- Execute Script --------

    protected List<System.Action<bool>> runningScripts;

    /// <summary>
    /// Editor coroutine wrapper for OptionHelper.RunScriptAsync.
    /// </summary>
    protected IEnumerator Execute(string path, string arguments, string input = null, System.Action<string> onOutput = null, System.Action<string> onError = null)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo();
        startInfo.FileName = path;
        startInfo.Arguments = arguments;
        return Execute(startInfo, input, onOutput, onError);
    }

    /// <summary>
    /// Editor coroutine wrapper for OptionHelper.RunScriptAsync.
    /// </summary>
    protected IEnumerator Execute(System.Diagnostics.ProcessStartInfo startInfo, string input = null, System.Action<string> onOutput = null, System.Action<string> onError = null)
    {
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        int? exitcode = null;
        var terminator = OptionHelper.RunScriptAsnyc(
            startInfo, input,
            (output) => {
                outputBuilder.AppendLine(output);
                if (onOutput != null) onOutput(output);
            },
            (error) => {
                errorBuilder.AppendLine(error);
                if (onError != null) onError(error);
            },
            (code) => {
                exitcode = code;
            }
        );

        if (runningScripts == null) runningScripts = new List<System.Action<bool>>();
        runningScripts.Add(terminator);

        while (exitcode == null) { yield return null; }

        runningScripts.Remove(terminator);

        // 137 happens for Kill() and 143 for CloseMainWindow(),
        // which means the script has ben canceled
        if (exitcode != 0 && exitcode != 137 && exitcode != 143) {
            Debug.LogError(string.Format(
                "{0}: Failed to execute {1}: {2}\nOutput: {3}",
                name, Path.GetFileName(startInfo.FileName),
                errorBuilder.ToString(), outputBuilder.ToString()
            ));
        }
        yield return exitcode;
    }

    // -------- Editor Coroutine --------

    /// <summary>
    /// Editor coroutine runner. It's quite different from Unity's coroutines:
    /// - You can only return null to pause a frame, no WaitForXXX
    /// - You can however return another coroutine IEnumerator and it'll finish that first
    /// - And you can use SubroutineResult to get that coroutine's last yielded value
    /// </summary>
    static public void RunCoroutine(IEnumerator routine)
    {
        Run(routine, true);
    }

    /// <summary>
    /// Get the last yielded value of a subroutine.
    /// This can only be called in the coroutine that yielded the subroutine and
    /// only between after the subroutine finished and before the parent coroutine
    /// yields again.
    /// </summary>
    static public T GetSubroutineResult<T>()
    {
        if (!hasLastRoutineValue) {
            throw new System.Exception("SubroutineResult can only be called in the parent routine right after the subroutine finished.");
        }

        if (lastRoutineValue != null && lastRoutineValue is T) {
            return (T)lastRoutineValue;
        }

        return default(T);
    }

    static List<IEnumerator> routines;
    static Dictionary<IEnumerator, IEnumerator> parentRoutines;
    static Dictionary<IEnumerator, object> lastRoutineValues;
    static bool hasLastRoutineValue;
    static object lastRoutineValue;

    /// <summary>
    /// Internal run method that can take already advanced routines.
    /// </summary>
    static bool Run(IEnumerator routine, bool advance)
    {
        // Check if the coroutine breaks immediately and don't bother scheduling it
        if (!advance || routine.MoveNext()) {
            if (routines == null) {
                routines = new List<IEnumerator>();
                lastRoutineValues = new Dictionary<IEnumerator, object>();
            }

            if (routines.Count == 0) {
                EditorApplication.update += Runner;
            }

            routines.Add(routine);
            ProcessRoutine(routines.Count - 1);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The runner update method.
    /// </summary>
    static void Runner()
    {
        // Stop the runner when there are no more active routines
        if (routines == null || routines.Count == 0) {
            EditorApplication.update -= Runner;
            return;
        }

        // Process routines from the back so we can add during the loop
        for (int i = routines.Count - 1; i >= 0; i--) {
            var routine = routines[i];

            if (routine.MoveNext()) {
                // Routine is running
                ProcessRoutine(i);

            } else {
                // Routine has finished
                routines.RemoveAt(i);
                StopRoutine(routine);
            }
        }
    }

    /// <summary>
    /// Process the yielded value of a coroutine.
    /// </summary>
    static void ProcessRoutine(int i)
    {
        var routine = routines[i];
        var value = lastRoutineValues[routine] = routine.Current;

        var subroutine = value as IEnumerator;
        if (subroutine != null && subroutine.MoveNext()) {
            // We got a subroutine, pause the routine and run the subroutine
            if (parentRoutines == null) {
                parentRoutines = new Dictionary<IEnumerator, IEnumerator>();
            }
            parentRoutines[subroutine] = routine;
            routines.RemoveAt(i);
            if (!Run(subroutine, false)) StopRoutine(subroutine);
        }
    }

    /// <summary>
    /// Stop a completed coroutine, continuing the parent routine if it exists.
    /// </summary>
    static void StopRoutine(IEnumerator routine)
    {
        if (parentRoutines != null && parentRoutines.ContainsKey(routine)) {
            // Continue parent routine of subroutine
            var parent = parentRoutines[routine];
            parentRoutines.Remove(routine);

            // Setting the subroutine's last value so it can be
            // accessed by the parent routine using SubroutineResult()
            hasLastRoutineValue = true;
            lastRoutineValue = lastRoutineValues[routine];
            if (!Run(parent, true)) StopRoutine(parent);
            lastRoutineValue = null;
            hasLastRoutineValue = false;
        }
        lastRoutineValues.Remove(routine);
    }
}

}
