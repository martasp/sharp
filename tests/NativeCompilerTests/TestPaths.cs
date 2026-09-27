using System;
using System.IO;

// Repo-root derivation shared by tests that were authored with an absolute Windows checkout path
// (D:\main\Pocs\NativeCompiler\). Derive the root from the test-host location instead so the suite
// runs on any machine and OS. Global namespace: used from both Namespace hosts in the suite.
internal static class TestPaths
{
    // <repo>/tests/NativeCompilerTests/bin/<config>/<tfm>/ -> up 5 lands on the repo root.
    public static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string Src(string project) => Path.Combine(RepoRoot, "src", project);

    public static string TestProjects(string name) =>
        Path.Combine(RepoRoot, "tests", "TestExampleProjects", name);

    public static string RootProjects(string name) =>
        Path.Combine(RepoRoot, "testProjects", name);
}