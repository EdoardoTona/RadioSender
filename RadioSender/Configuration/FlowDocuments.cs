using RadioSender.Flow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RadioSender.Configuration;

public sealed record DocumentSnapshot(Guid Id, string Path, long Revision, FlowDocument Document);

public sealed class FlowDocuments : IDisposable
{
  private sealed record Session(DocumentSnapshot Snapshot, string Hash);
  private readonly Dictionary<Guid, Session> _sessions = [];
  private readonly SemaphoreSlim _gate = new(1, 1);
  public void Dispose() => _gate.Dispose();
  private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
  private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
  private static DocumentSnapshot Copy(DocumentSnapshot snapshot) => snapshot with { Document = FlowJson.Clone(snapshot.Document) };
  private Session Get(Guid id) => _sessions.GetValueOrDefault(id) ?? throw new FlowException("This document session no longer exists. Open the file again.", 404);
  private static string FullPath(string path)
  {
    if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
      throw new FlowException("Choose an absolute file path ending in .json.");
    var full = System.IO.Path.GetFullPath(path);
    if (!Directory.Exists(System.IO.Path.GetDirectoryName(full))) throw new FlowException("The selected folder does not exist.");
    return full;
  }

  public async Task<DocumentSnapshot> OpenAsync(string path, bool create, CancellationToken ct = default)
  {
    path = FullPath(path);
    await _gate.WaitAsync(ct);
    try
    {
      var existing = _sessions.Values.FirstOrDefault(s => string.Equals(s.Snapshot.Path, path, PathComparison));
      if (existing != null && !create)
      {
        // Reload from disk only when explicitly opening the file. Existing editors retain revision protection.
        var bytes = await ReadBytesAsync(path, ct);
        if (Hash(bytes) != existing.Hash)
        {
          var updated = existing.Snapshot with { Revision = existing.Snapshot.Revision + 1, Document = FlowJson.Read(Encoding.UTF8.GetString(bytes)) };
          _sessions[updated.Id] = new(updated, Hash(bytes));
          return Copy(updated);
        }
        return Copy(existing.Snapshot);
      }
      if (_sessions.Count >= 32) throw new FlowException("Too many open documents. Restart the application to release document sessions.", 409);
      byte[] content;
      if (create)
      {
        content = JsonSerializer.SerializeToUtf8Bytes(new FlowDocument(), FlowJson.Options);
        await WriteAtomicAsync(path, content, null, ct);
      }
      else content = await ReadBytesAsync(path, ct);
      var document = FlowJson.Read(Encoding.UTF8.GetString(content));
      var snapshot = new DocumentSnapshot(Guid.NewGuid(), path, 1, document);
      _sessions.Add(snapshot.Id, new(snapshot, Hash(content)));
      return Copy(snapshot);
    }
    finally { _gate.Release(); }
  }

  private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken ct)
  {
    if (new FileInfo(path).Length > FlowJson.MaxDocumentBytes) throw new FlowException("The document exceeds the 2 MB limit.");
    return await File.ReadAllBytesAsync(path, ct);
  }

  public async Task<DocumentSnapshot> ReadAsync(Guid id, CancellationToken ct = default)
  {
    await _gate.WaitAsync(ct);
    try { return Copy(Get(id).Snapshot); }
    finally { _gate.Release(); }
  }

  public async Task<DocumentSnapshot> SaveAsync(Guid id, long expectedRevision, FlowDocument? document = null, string? newPath = null, CancellationToken ct = default)
  {
    await _gate.WaitAsync(ct);
    try
    {
      var session = Get(id);
      var path = newPath == null ? session.Snapshot.Path : FullPath(newPath);
      var samePath = string.Equals(path, session.Snapshot.Path, PathComparison);
      var stale = session.Snapshot.Revision != expectedRevision;
      if (stale && (samePath || document == null)) throw new FlowException("Another editor changed this document. Reload it or save a copy.", 409);
      if (!samePath && _sessions.Values.Any(s => string.Equals(s.Snapshot.Path, path, PathComparison)))
        throw new FlowException("The destination is already open. Choose another file.", 409);
      document = FlowJson.Clone(document ?? session.Snapshot.Document);
      var bytes = JsonSerializer.SerializeToUtf8Bytes(document, FlowJson.Options);
      await WriteAtomicAsync(path, bytes, samePath ? session.Hash : null, ct);
      var changed = Hash(bytes) != session.Hash || !samePath;
      // A stale editor can recover its own draft as a separate document without replacing another editor's session.
      var updated = new DocumentSnapshot(stale ? Guid.NewGuid() : id, path, stale ? 1 : session.Snapshot.Revision + (changed ? 1 : 0), document);
      _sessions[updated.Id] = new(updated, Hash(bytes));
      return Copy(updated);
    }
    finally { _gate.Release(); }
  }

  private static async Task WriteAtomicAsync(string path, byte[] bytes, string? expectedHash, CancellationToken ct)
  {
    async Task CheckAsync()
    {
      if (File.Exists(path))
      {
        if (expectedHash == null) throw new FlowException("A file already exists at this path. Choose a new name.", 409);
        if (Hash(await ReadBytesAsync(path, ct)) != expectedHash)
          throw new FlowException("The file changed outside RadioSender. Open it again to reload, or use Save As.", 409);
      }
      else if (expectedHash != null) throw new FlowException("The file was removed outside RadioSender. Use Save As.", 409);
    }
    await CheckAsync();
    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
      await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
      { await file.WriteAsync(bytes, ct); file.Flush(flushToDisk: true); }
      await CheckAsync();
      if (expectedHash != null) File.Copy(path, path + ".bak", overwrite: true);
      File.Move(temporary, path, overwrite: expectedHash != null);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
  }
}
