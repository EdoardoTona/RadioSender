using RadioSender.Flow.Modules;
using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using RadioSender.Hosts.Common;

namespace Test.RadioSender;

[TestFixture]
public class TestFileTargetSuppression
{
  private static Punch Punch(bool cancellation = false, CompetitorStatus status = CompetitorStatus.Unknown) => new(
    CompetitorId: "7",
    Control: 90,
    SourceId: "test",
    ReceivedAt: DateTimeOffset.UtcNow,
    Time: new DateTime(2026, 07, 24, 10, 1, 3, 880),
    Cancellation: cancellation,
    CompetitorStatus: status);

  [Test]
  public async Task FormatWithoutCancellationPlaceholder_CancellationPunch_IsSuppressed()
  {
    var path = Path.Combine(Path.GetTempPath(), $"radiosender-test-{Guid.NewGuid()}.csv");
    try
    {
      await using var target = new FileFlowModule(new FileSettings { Path = path, Format = "{CompetitorId};{Control}{CRLF}" }, new("file", Path.GetTempPath(), new CapturingOutput()));
      await target.StartAsync(default);

      await target.SendAsync(Punch(cancellation: true), default);

      // FileMode.Append creates an empty file even when nothing ends up written to it.
      Assert.That(System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "", Is.Empty);
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }

  [Test]
  public async Task FormatWithCancellationPlaceholder_CancellationPunch_IsWritten()
  {
    var path = Path.Combine(Path.GetTempPath(), $"radiosender-test-{Guid.NewGuid()}.csv");
    try
    {
      await using var target = new FileFlowModule(new FileSettings { Path = path, Format = "{CompetitorId};{Control};{Cancellation}{CRLF}" }, new("file", Path.GetTempPath(), new CapturingOutput()));
      await target.StartAsync(default);

      await target.SendAsync(Punch(cancellation: true), default);

      Assert.That(System.IO.File.Exists(path), Is.True);
      Assert.That(System.IO.File.ReadAllText(path), Does.Contain("ANN"));
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }

  [Test]
  public async Task FormatWithoutStatusPlaceholder_StatusPunch_IsSuppressed()
  {
    var path = Path.Combine(Path.GetTempPath(), $"radiosender-test-{Guid.NewGuid()}.csv");
    try
    {
      await using var target = new FileFlowModule(new FileSettings { Path = path, Format = "{CompetitorId};{Control};{Time:HH:mm:ss.fff}{CRLF}" }, new("file", Path.GetTempPath(), new CapturingOutput()));
      await target.StartAsync(default);

      await target.SendAsync(Punch(status: CompetitorStatus.DNS), default);

      Assert.That(System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "", Is.Empty);
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }

  [Test]
  public async Task FormatWithStatusPlaceholder_StatusPunch_IsWritten()
  {
    var path = Path.Combine(Path.GetTempPath(), $"radiosender-test-{Guid.NewGuid()}.csv");
    try
    {
      await using var target = new FileFlowModule(new FileSettings { Path = path, Format = "{CompetitorId};{Control};{Time:HH:mm:ss.fff};{Status}{CRLF}" }, new("file", Path.GetTempPath(), new CapturingOutput()));
      await target.StartAsync(default);

      await target.SendAsync(Punch(status: CompetitorStatus.DNS), default);

      Assert.That(System.IO.File.Exists(path), Is.True);
      var content = System.IO.File.ReadAllText(path);
      Assert.That(content, Does.Contain("DNS"));
      Assert.That(content, Does.Contain("10:01:03.880")); // real time preserved, no sentinel rewrite for File
    }
    finally
    {
      if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
    }
  }
}
