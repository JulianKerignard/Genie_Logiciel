# CryptoSoft

File-encryption companion binary for EasySave. Implements the CLI contract
documented in [`../docs/cryptosoft-integration.md`](../docs/cryptosoft-integration.md):
EasySave spawns it once per file flagged in `encrypted_extensions`, the binary
encrypts the source to the target, and the elapsed time in ms is returned as
the process exit code (`>= 0` success; negative values are reserved error
codes from `ExitCodes.cs`).

## Cahier V3 — Mono-Instance

The CdC requires that CryptoSoft "ne peut être exécuté en simultanée sur un
même ordinateur". The binary owns a named system Mutex
(`Global\ProSoft.CryptoSoft.SingleInstance`); a second launch returns `-2`
(`AlreadyRunning`) immediately without touching the target file.

## Architecture

| File | Responsibility |
|---|---|
| `Program.cs` | Entry point — parses args, wires the dependency graph, returns the exit code. |
| `CryptoSoftRunner.cs` | Pure orchestrator: acquire gate → read → encrypt → write → return elapsed ms. |
| `ICryptoAlgorithm.cs` + `XorCryptoAlgorithm.cs` | Strategy. Swap XOR for AES without touching the orchestrator. |
| `IMonoInstanceGate.cs` + `SystemMutexGate.cs` | Cross-process gate. The interface lets `CryptoSoftRunner` stay unit-testable with fakes. |
| `ExitCodes.cs` | Named exit-code constants. |

The split mirrors the SOLID principles the team relies on across the
EasySave solution: each class has one reason to change, the runner depends
only on the two narrow interfaces, and adding a real cryptographic
primitive later is a 1-file addition.

## Build & run

```bash
dotnet build CryptoSoft/CryptoSoft.csproj
dotnet run --project CryptoSoft -- <source> <target>
echo "exit=$?"  # 0..N = elapsed ms; -1..-4 = errors per ExitCodes.cs
```

## Encryption details

Demo-grade single-byte repeating XOR (key = `0xA5`). Symmetric — running
CryptoSoft on the encrypted file restores the plaintext. A production
deployment would replace `XorCryptoAlgorithm` with an AES-backed
`ICryptoAlgorithm` and source the key from a secret store.
