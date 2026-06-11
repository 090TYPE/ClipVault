using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

/// Raw payload captured from the OS clipboard, before persistence.
public record ClipboardReadResult(
    ClipType Type,
    string? Text,
    byte[]? ImagePng,
    IReadOnlyList<string>? FilePaths,
    string? SourceApp);
