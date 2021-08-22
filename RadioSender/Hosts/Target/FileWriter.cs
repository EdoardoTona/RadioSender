using System;
using System.IO;
using System.Text;

namespace RadioSender.Hosts.Target
{
  public sealed class FileWriter : IDisposable
  {
    private readonly FileInfo _fileInfo;
    private readonly StreamWriter _sr;

    public FileWriter(string path)
    {
      _fileInfo = new FileInfo(path);

      if (_fileInfo.Directory == null || _fileInfo.DirectoryName == null)
        throw new ArgumentException("Invalid path");

      if (!_fileInfo.Directory.Exists)
        Directory.CreateDirectory(_fileInfo.DirectoryName);

      // TODO: handle network files (with broken connection)
      _sr = new(_fileInfo.FullName, append: true, Encoding.UTF8);
      _sr.AutoFlush = true;
    }

    public void Dispose()
    {
      _sr?.Close();
      _sr?.Dispose();
    }

    public void Write(string text)
    {
      _sr.Write(text);

    }
  }
}
