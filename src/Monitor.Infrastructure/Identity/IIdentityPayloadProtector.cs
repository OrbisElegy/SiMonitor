// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Identity;

public interface IIdentityPayloadProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose);

    public string LookupToken(string value, string purpose);
}
