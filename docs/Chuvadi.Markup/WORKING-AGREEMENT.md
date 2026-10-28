# Chuvadi.Markup — Working Agreement

The operating protocol for building Chuvadi.Markup together. Recorded so it is never forgotten.

1. **No guessing.** Never assume a file's contents or a convention. Check the actual file in the
   cloned repo, or ask for an upload. Every decision is grounded in real files.
2. **Whole files, no manual edits.** Deliverables are complete files or full PowerShell commands to
   copy-paste — never snippets or partial patches for the maintainer to splice by hand.
3. **Always the full command set.** Every delivery ships with commands to expand, copy, build, test
   and run.
4. **Git at every milestone.** Each important point comes with commit + push commands, and cleanup
   commands afterwards.
5. **Visual verification wins.** Whenever a file is generated, the actual output file is provided for
   the maintainer to inspect. The maintainer's visual check supersedes any automated result.
6. **Reproducible environment.** The assistant installs the .NET SDK and clones the repo in its own
   environment to work against real code.
7. **Step-by-step repo help.** The assistant guides creating and updating the local repo with explicit
   commands.
8. **Green before delivery.** The assistant builds, tests and runs in its environment and only delivers
   once it succeeds. (Chuvadi.Markup is fully portable, so everything is built and tested on Linux too;
   the maintainer's look at the preview pages in Edge / WebView2 on Windows is the final word.)
9. **Push, then pause.** For pushes, the assistant stops at the `git push` step. After CI runs all tests
   and the maintainer merges, the maintainer confirms; only then does the assistant give cleanup commands.
10. **Libraries are complete.** A library exposes the entire option set, never a partial or one-directional
    subset. Where there is left there is also right, top, bottom and centre. No "this side only" choices.
11. These points live in this document so they are not forgotten.
12. Work proceeds incrementally, each step independently buildable and green.
