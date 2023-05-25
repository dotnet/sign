#pragma warning disable IDE0073 // The file header does not match the required text
// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;
[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "This program only supports Windows at this time.", Scope = "member", Target = "~M:Sign.Core.ManifestSigner.Sign(System.IO.FileInfo,System.Security.Cryptography.X509Certificates.X509Certificate2,System.Security.Cryptography.RSA,Sign.Core.SignOptions)")]
