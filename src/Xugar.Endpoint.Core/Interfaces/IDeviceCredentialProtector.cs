namespace Xugar.Endpoint.Core.Interfaces;

public interface IDeviceCredentialProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}
