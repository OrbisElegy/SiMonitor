// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Identity;

public interface IPlatformKeyProtector
{
    public string ProtectorId { get; }

    public byte[] Protect(ReadOnlySpan<byte> plaintext);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload);
}
