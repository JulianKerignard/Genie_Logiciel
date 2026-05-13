using CryptoSoft;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CryptoSoft <source-file> <target-file>");
    return ExitCodes.InvalidArguments;
}

// Demo-grade XOR key. A production deployment would derive the key from
// a secret store / environment variable / wrapped key file — never a
// hard-coded constant.
const byte XorKey = 0xA5;

using var gate = new SystemMutexGate();
var algorithm = new XorCryptoAlgorithm(XorKey);
var runner = new CryptoSoftRunner(algorithm, gate);

return runner.Run(args[0], args[1]);
