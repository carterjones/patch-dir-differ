# patch-dir-differ

Perform a diff of two directories, comparing the differing files of both
directories. Plaintext files will be analyzed directly. PE files (dll, exe,
sys) will be disassembled, followed by a plaintext diff of the disassembly.

The output can be found in diff.html in the same directory as the
PatchDirDiffer.exe file.

    usage: PatchDirDiffer <unpatched dir> <patched dir>
