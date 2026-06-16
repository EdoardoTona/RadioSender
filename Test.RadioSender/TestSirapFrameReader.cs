using NUnit.Framework;
using RadioSender.Hosts.Source.SIRAP;
using System;
using System.Collections.Generic;

namespace Test.RadioSender;

public class TestSirapFrameReader
{
  [Test]
  public void WaitsForCompleteV1FrameSplitAcrossTcpPackets()
  {
    var buffer = new List<byte>();
    buffer.AddRange([0x00, 0x28, 0x00, 0x32, 0x64, 0x0A]);
    SirapProtocolVersion? version = null;

    var hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out _, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.False);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(buffer, Has.Count.EqualTo(6));
    });

    buffer.AddRange([0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xDA, 0x25, 0x00, 0x00]);

    hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var frame, out discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.True);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(frame.Version, Is.EqualTo(SirapProtocolVersion.V1));
      Assert.That(frame.Data, Has.Length.EqualTo(SirapFrameReader.V1RecordLength));
      Assert.That(version, Is.EqualTo(SirapProtocolVersion.V1));
      Assert.That(buffer, Is.Empty);
    });
  }

  [Test]
  public void ReadsMultipleCompleteV1FramesFromSameTcpPacket()
  {
    var firstFrame = CreateV1Frame(0xDA, 0x25);
    var secondFrame = CreateV1Frame(0x72, 0x29);
    var buffer = new List<byte>();
    buffer.AddRange(firstFrame);
    buffer.AddRange(secondFrame);
    SirapProtocolVersion? version = null;

    var hasFirstFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var firstResult, out var firstDiscardedBytes);
    var hasSecondFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var secondResult, out var secondDiscardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFirstFrame, Is.True);
      Assert.That(firstDiscardedBytes, Is.EqualTo(0));
      Assert.That(firstResult.Version, Is.EqualTo(SirapProtocolVersion.V1));
      Assert.That(firstResult.Data, Is.EqualTo(firstFrame));
      Assert.That(hasSecondFrame, Is.True);
      Assert.That(secondDiscardedBytes, Is.EqualTo(0));
      Assert.That(secondResult.Version, Is.EqualTo(SirapProtocolVersion.V1));
      Assert.That(secondResult.Data, Is.EqualTo(secondFrame));
      Assert.That(buffer, Is.Empty);
    });
  }

  [Test]
  public void WaitsForCompleteV2Frame()
  {
    var buffer = new List<byte>
    {
      11,
      (byte)'R', (byte)'a', (byte)'d', (byte)'i', (byte)'o', (byte)'s',
      (byte)'e', (byte)'n', (byte)'d', (byte)'e', (byte)'r'
    };
    SirapProtocolVersion? version = null;

    var hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out _, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.False);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(buffer, Has.Count.EqualTo(12));
    });
  }

  [Test]
  public void ReadsCompleteV2Frame()
  {
    var sourceName = "Radiosender";
    var v2Frame = CreateV2Frame(sourceName);
    var buffer = new List<byte>();
    buffer.AddRange(v2Frame);
    SirapProtocolVersion? version = null;

    var hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var frame, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.True);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(frame.Version, Is.EqualTo(SirapProtocolVersion.V2));
      Assert.That(frame.Data, Is.EqualTo(v2Frame));
      Assert.That(version, Is.EqualTo(SirapProtocolVersion.V2));
      Assert.That(buffer, Is.Empty);
    });
  }

  [Test]
  public void ReadsCompleteV2FrameWithEmptyName()
  {
    var v2Frame = CreateV2Frame(string.Empty);
    var buffer = new List<byte>();
    buffer.AddRange(v2Frame);
    SirapProtocolVersion? version = null;

    var hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var frame, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.True);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(frame.Version, Is.EqualTo(SirapProtocolVersion.V2));
      Assert.That(frame.Data, Is.EqualTo(v2Frame));
      Assert.That(version, Is.EqualTo(SirapProtocolVersion.V2));
      Assert.That(buffer, Is.Empty);
    });
  }

  [Test]
  public void DiscardsInvalidStartBytesInOneBatchBeforeFrame()
  {
    var v1Frame = CreateV1Frame(0xDA, 0x25);
    var buffer = new List<byte> { 0x21, 0x22, 0xFE };
    buffer.AddRange(v1Frame);
    SirapProtocolVersion? version = null;

    var hasFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var frame, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFrame, Is.True);
      Assert.That(discardedBytes, Is.EqualTo(3));
      Assert.That(frame.Version, Is.EqualTo(SirapProtocolVersion.V1));
      Assert.That(frame.Data, Is.EqualTo(v1Frame));
      Assert.That(buffer, Is.Empty);
    });
  }

  [Test]
  public void V2VersionLatchWaitsForFullSubsequentEmptyNameFrame()
  {
    var firstFrame = CreateV2Frame("Radiosender");
    var emptyNameFrame = CreateV2Frame(string.Empty);
    var buffer = new List<byte>();
    buffer.AddRange(firstFrame);
    buffer.AddRange(emptyNameFrame.AsSpan(0, 15));
    SirapProtocolVersion? version = null;

    var hasFirstFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out var firstResult, out _);
    var hasSecondFrame = SirapFrameReader.TryTakeFrame(buffer, ref version, out _, out var discardedBytes);

    Assert.Multiple(() =>
    {
      Assert.That(hasFirstFrame, Is.True);
      Assert.That(firstResult.Version, Is.EqualTo(SirapProtocolVersion.V2));
      Assert.That(hasSecondFrame, Is.False);
      Assert.That(discardedBytes, Is.EqualTo(0));
      Assert.That(buffer, Has.Count.EqualTo(15));
      Assert.That(version, Is.EqualTo(SirapProtocolVersion.V2));
    });
  }

  private static byte[] CreateV1Frame(byte timeLow, byte timeHigh)
  {
    return
    [
      0x00, 0x28, 0x00, 0x32, 0x64, 0x0A, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, timeLow, timeHigh, 0x00, 0x00
    ];
  }

  private static byte[] CreateV2Frame(string sourceName)
  {
    var frame = new byte[SirapFrameReader.V2RecordLength];
    frame[0] = (byte)sourceName.Length;

    for (var i = 0; i < sourceName.Length; i++)
      frame[i + 1] = (byte)sourceName[i];

    frame[21] = 0x00;
    frame[22] = 0x28;
    frame[24] = 0x32;
    frame[25] = 0x64;
    frame[26] = 0x0A;
    frame[28] = 0xFF;
    frame[29] = 0xFF;
    frame[30] = 0xFF;
    frame[31] = 0xFF;
    frame[32] = 0xDA;
    frame[33] = 0x25;

    return frame;
  }
}
