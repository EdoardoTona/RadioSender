using RadioSender.Hosts.Common;
using RadioSender.Hosts.Target.SIRAP;
using System.Text.RegularExpressions;

namespace ManualSender
{
  public partial class Form1 : Form
  {
    SirapClient sirapClient = new SirapClient();
    public Form1()
    {
      InitializeComponent();

      panel1.Enabled = false;
    }

    private void textBox3_TextChanged(object sender, EventArgs e)
    {

    }

    private void rBtnTime_CheckedChanged(object sender, EventArgs e)
    {
      if (rBtnTime.Checked)
      {
        panel1.Enabled = true;
      }
      else
      {
        panel1.Enabled = false;
      }
    }

    private void timeBox_TextChanged(object sender, EventArgs e)
    {
      var regex = TimeRegex();
      if (regex.IsMatch(timeBox.Text))
      {
        timeBox.ForeColor = Color.Black;
      }
      else
      {
        timeBox.ForeColor = Color.Red;
      }
    }


    private void button1_Click(object sender, EventArgs e)
    {
      try
      {
        if (string.IsNullOrEmpty(identifierBox.Text) || !int.TryParse(identifierBox.Text, out _))
        {
          MessageBox.Show("Invalid card/bib");
          return;
        }

        DateTime time;
        int control = -1;
        PunchControlType punchControlType = PunchControlType.Unknown;
        CompetitorStatus competitorStatus = CompetitorStatus.Unknown;
        if (rBtnTime.Checked)
        {
          if (!TimeRegex().IsMatch(timeBox.Text))
          {
            MessageBox.Show("Invalid time");
            return;
          }
          time = DateTime.Parse(timeBox.Text);

          if (rBtnControl.Checked)
          {
            punchControlType = PunchControlType.Control;
            if (!ControlRegex().IsMatch(controlBox.Text) || !int.TryParse(controlBox.Text, out control))
            {
              MessageBox.Show("Invalid control code");
              return;
            }
          }
          else if (rBtnStart.Checked)
          {
            punchControlType = PunchControlType.Start;
            if (!ControlRegex().IsMatch(startBox.Text) || !int.TryParse(startBox.Text, out control))
            {
              MessageBox.Show("Invalid start code");
              return;
            }
          }
          else if (rBtnFinish.Checked)
          {
            punchControlType = PunchControlType.Finish;
            if (!ControlRegex().IsMatch(finishBox.Text) || !int.TryParse(finishBox.Text, out control))
            {
              MessageBox.Show("Invalid finish code");
              return;
            }
          }

          if (control < 1)
          {
            MessageBox.Show("Missing control");
            return;
          }
        }
        else
        {
          time = DateTime.Now;

          control = 5;

          if (rBtnDNF.Checked)
          {
            competitorStatus = CompetitorStatus.DNF;
          }
          else if (rBtnDNS.Checked)
          {
            competitorStatus = CompetitorStatus.DNS;
          }
          else if (rBtnDSQ.Checked)
          {
            competitorStatus = CompetitorStatus.DSQ;
          }
          else if (rBtnMP.Checked)
          {
            competitorStatus = CompetitorStatus.MP;
          }
          else if (rBtnOT.Checked)
          {
            competitorStatus = CompetitorStatus.OverTime;
          }

        }

        var punch = new Punch(identifierBox.Text, time, control, "Man", DateTimeOffset.UtcNow, punchControlType, competitorStatus);

        sirapClient.SendDispatch(addressBox.Text, punch);

        MessageBox.Show("Sent!");
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
      }
    }

    [GeneratedRegex(@"^(?:[0-9]|[1-9][0-9]|1\d{2}|2[0-4]\d|25[0-5])$")]
    private static partial Regex ControlRegex();
    [GeneratedRegex(@"^(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:,\d{1,3})?$")]
    private static partial Regex TimeRegex();

    private void controlBox_TextChanged(object sender, EventArgs e)
    {
      var box = sender as TextBox;
      if (box == null) return;

      var regex = ControlRegex();
      if (regex.IsMatch(box.Text))
      {
        box.ForeColor = Color.Black;
      }
      else
      {
        box.ForeColor = Color.Red;
      }
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
      sirapClient.Disconnect();
    }
  }
}
