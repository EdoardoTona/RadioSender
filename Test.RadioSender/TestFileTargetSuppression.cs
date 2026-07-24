using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RadioSender.Hosts.Common;
using RadioSender.Hosts.Common.Filters;
using RadioSender.Hosts.Enrichment;
using RadioSender.Hosts.Target.File;

namespace Test.RadioSender;

[TestFixture]
public class TestFileTargetSuppression
{
  private sealed class StubMonitor(FiltersConfiguration value) : IOptionsMonitor<FiltersConfiguration>
  {
    public FiltersConfiguration CurrentValue => value;
    public FiltersConfiguration Get(string? name) => value;
    public IDisposable? OnChange(Action<FiltersConfiguration, string?> listener) => null;
  }

  private static FilterService BuildFilterService()
    => new(new StubMonitor(new FiltersConfiguration { List = [] }), Array.Empty<IEnrichmentSource>());

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
      var target = new FileTarget(BuildFilterService(), new FileConfiguration { Path = path, Format = "{CompetitorId};{Control}{CRLF}" });

      await target.SendDispatch(new PunchDispatch(Punches: [Punch(cancellation: true)]));

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
      var target = new FileTarget(BuildFilterService(), new FileConfiguration { Path = path, Format = "{CompetitorId};{Control};{Cancellation}{CRLF}" });

      await target.SendDispatch(new PunchDispatch(Punches: [Punch(cancellation: true)]));

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
      var target = new FileTarget(BuildFilterService(), new FileConfiguration { Path = path, Format = "{CompetitorId};{Control};{Time:HH:mm:ss.fff}{CRLF}" });

      await target.SendDispatch(new PunchDispatch(Punches: [Punch(status: CompetitorStatus.DNS)]));

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
      var target = new FileTarget(BuildFilterService(), new FileConfiguration { Path = path, Format = "{CompetitorId};{Control};{Time:HH:mm:ss.fff};{Status}{CRLF}" });

      await target.SendDispatch(new PunchDispatch(Punches: [Punch(status: CompetitorStatus.DNS)]));

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
