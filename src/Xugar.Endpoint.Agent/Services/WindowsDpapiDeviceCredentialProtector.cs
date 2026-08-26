using System.Security.Cryptography;
using System.Text;
using Xugar.Endpoint.Core.Interfaces;

namespace Xugar.Endpoint.Agent.Services;

public sealed class WindowsDpapiDeviceCredentialProtector : IDeviceCredentialProtector
{
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("Xugar.Endpoint.Agent.DeviceCredential.v1");

    public byte[] Protect(ReadOnlySpan<byte> plaintext) =>
        ProtectedData.Protect(plaintext.ToArray(), OptionalEntropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData) =>
        ProtectedData.Unprotect(protectedData.ToArray(), OptionalEntropy, DataProtectionScope.CurrentUser);
}
