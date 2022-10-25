# Breakpoints Per Branch

A Visual Studio Extension that associates your Visual Studio debugging breakpoints with Git branches.

As you switch between Git branches, the breakpoints associated with that git branch are restored.

This should be considered proof-of-concept code -- no real testing has been done.

## How it works

1. When you launch the debugger, the current set of breakpoints are saved to your temp directory. The temp filename corresponds to the solution and branch name.
1. When you switch git branches, if there is a set of saved breakpoints for that branch, the list of breakpoints is reset to the saved list.
