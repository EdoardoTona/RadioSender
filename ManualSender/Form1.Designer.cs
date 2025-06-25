namespace ManualSender
{
  partial class Form1
  {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
      {
        components.Dispose();
      }
      base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
      label1 = new Label();
      addressBox = new TextBox();
      identifierBox = new TextBox();
      rBtnTime = new RadioButton();
      rBtnDNS = new RadioButton();
      rBtnDNF = new RadioButton();
      rBtnDSQ = new RadioButton();
      rBtnMP = new RadioButton();
      rBtnOT = new RadioButton();
      controlBox = new TextBox();
      rBtnControl = new RadioButton();
      rBtnStart = new RadioButton();
      rBtnFinish = new RadioButton();
      button1 = new Button();
      label2 = new Label();
      panel1 = new Panel();
      finishBox = new TextBox();
      startBox = new TextBox();
      timeBox = new TextBox();
      panel1.SuspendLayout();
      SuspendLayout();
      // 
      // label1
      // 
      label1.AutoSize = true;
      label1.Location = new Point(34, 37);
      label1.Name = "label1";
      label1.Size = new Size(153, 37);
      label1.TabIndex = 0;
      label1.Text = "Destination";
      // 
      // addressBox
      // 
      addressBox.Location = new Point(198, 37);
      addressBox.Name = "addressBox";
      addressBox.Size = new Size(393, 43);
      addressBox.TabIndex = 1;
      addressBox.Text = "localhost:10000";
      // 
      // identifierBox
      // 
      identifierBox.Font = new Font("Segoe UI", 28F, FontStyle.Regular, GraphicsUnit.Point, 0);
      identifierBox.Location = new Point(198, 108);
      identifierBox.Name = "identifierBox";
      identifierBox.Size = new Size(393, 119);
      identifierBox.TabIndex = 2;
      identifierBox.Text = "9998999";
      identifierBox.TextAlign = HorizontalAlignment.Center;
      // 
      // rBtnTime
      // 
      rBtnTime.AutoSize = true;
      rBtnTime.Location = new Point(38, 306);
      rBtnTime.Name = "rBtnTime";
      rBtnTime.Size = new Size(106, 41);
      rBtnTime.TabIndex = 4;
      rBtnTime.TabStop = true;
      rBtnTime.Text = "Time";
      rBtnTime.UseVisualStyleBackColor = true;
      rBtnTime.CheckedChanged += rBtnTime_CheckedChanged;
      // 
      // rBtnDNS
      // 
      rBtnDNS.AutoSize = true;
      rBtnDNS.Location = new Point(491, 305);
      rBtnDNS.Name = "rBtnDNS";
      rBtnDNS.Size = new Size(101, 41);
      rBtnDNS.TabIndex = 5;
      rBtnDNS.TabStop = true;
      rBtnDNS.Text = "DNS";
      rBtnDNS.UseVisualStyleBackColor = true;
      // 
      // rBtnDNF
      // 
      rBtnDNF.AutoSize = true;
      rBtnDNF.Location = new Point(491, 352);
      rBtnDNF.Name = "rBtnDNF";
      rBtnDNF.Size = new Size(100, 41);
      rBtnDNF.TabIndex = 6;
      rBtnDNF.TabStop = true;
      rBtnDNF.Text = "DNF";
      rBtnDNF.UseVisualStyleBackColor = true;
      // 
      // rBtnDSQ
      // 
      rBtnDSQ.AutoSize = true;
      rBtnDSQ.Location = new Point(491, 399);
      rBtnDSQ.Name = "rBtnDSQ";
      rBtnDSQ.Size = new Size(101, 41);
      rBtnDSQ.TabIndex = 7;
      rBtnDSQ.TabStop = true;
      rBtnDSQ.Text = "DSQ";
      rBtnDSQ.UseVisualStyleBackColor = true;
      // 
      // rBtnMP
      // 
      rBtnMP.AutoSize = true;
      rBtnMP.Location = new Point(490, 446);
      rBtnMP.Name = "rBtnMP";
      rBtnMP.Size = new Size(87, 41);
      rBtnMP.TabIndex = 8;
      rBtnMP.TabStop = true;
      rBtnMP.Text = "MP";
      rBtnMP.UseVisualStyleBackColor = true;
      // 
      // rBtnOT
      // 
      rBtnOT.AutoSize = true;
      rBtnOT.Location = new Point(491, 493);
      rBtnOT.Name = "rBtnOT";
      rBtnOT.Size = new Size(81, 41);
      rBtnOT.TabIndex = 9;
      rBtnOT.TabStop = true;
      rBtnOT.Text = "OT";
      rBtnOT.UseVisualStyleBackColor = true;
      // 
      // controlBox
      // 
      controlBox.Location = new Point(156, 114);
      controlBox.Name = "controlBox";
      controlBox.Size = new Size(106, 43);
      controlBox.TabIndex = 10;
      controlBox.TextChanged += controlBox_TextChanged;
      // 
      // rBtnControl
      // 
      rBtnControl.AutoSize = true;
      rBtnControl.Location = new Point(13, 116);
      rBtnControl.Name = "rBtnControl";
      rBtnControl.Size = new Size(137, 41);
      rBtnControl.TabIndex = 11;
      rBtnControl.TabStop = true;
      rBtnControl.Text = "Control";
      rBtnControl.UseVisualStyleBackColor = true;
      // 
      // rBtnStart
      // 
      rBtnStart.AutoSize = true;
      rBtnStart.Location = new Point(13, 69);
      rBtnStart.Name = "rBtnStart";
      rBtnStart.Size = new Size(102, 41);
      rBtnStart.TabIndex = 12;
      rBtnStart.TabStop = true;
      rBtnStart.Text = "Start";
      rBtnStart.UseVisualStyleBackColor = true;
      // 
      // rBtnFinish
      // 
      rBtnFinish.AutoSize = true;
      rBtnFinish.Location = new Point(13, 163);
      rBtnFinish.Name = "rBtnFinish";
      rBtnFinish.Size = new Size(116, 41);
      rBtnFinish.TabIndex = 13;
      rBtnFinish.TabStop = true;
      rBtnFinish.Text = "Finish";
      rBtnFinish.UseVisualStyleBackColor = true;
      // 
      // button1
      // 
      button1.Location = new Point(34, 610);
      button1.Name = "button1";
      button1.Size = new Size(557, 52);
      button1.TabIndex = 14;
      button1.Text = "Send";
      button1.UseVisualStyleBackColor = true;
      button1.Click += button1_Click;
      // 
      // label2
      // 
      label2.AutoSize = true;
      label2.Location = new Point(34, 108);
      label2.Name = "label2";
      label2.Size = new Size(123, 37);
      label2.TabIndex = 15;
      label2.Text = "Identifier";
      // 
      // panel1
      // 
      panel1.Controls.Add(finishBox);
      panel1.Controls.Add(startBox);
      panel1.Controls.Add(timeBox);
      panel1.Controls.Add(rBtnControl);
      panel1.Controls.Add(controlBox);
      panel1.Controls.Add(rBtnStart);
      panel1.Controls.Add(rBtnFinish);
      panel1.Location = new Point(60, 352);
      panel1.Name = "panel1";
      panel1.Size = new Size(295, 213);
      panel1.TabIndex = 16;
      // 
      // finishBox
      // 
      finishBox.Location = new Point(156, 161);
      finishBox.Name = "finishBox";
      finishBox.Size = new Size(106, 43);
      finishBox.TabIndex = 16;
      finishBox.TextChanged += controlBox_TextChanged;
      // 
      // startBox
      // 
      startBox.Location = new Point(156, 69);
      startBox.Name = "startBox";
      startBox.Size = new Size(106, 43);
      startBox.TabIndex = 15;
      startBox.TextChanged += controlBox_TextChanged;
      // 
      // timeBox
      // 
      timeBox.Location = new Point(13, 20);
      timeBox.Name = "timeBox";
      timeBox.PlaceholderText = "hh:mm:ss,fff";
      timeBox.Size = new Size(249, 43);
      timeBox.TabIndex = 14;
      timeBox.TextChanged += timeBox_TextChanged;
      // 
      // Form1
      // 
      AutoScaleDimensions = new SizeF(15F, 37F);
      AutoScaleMode = AutoScaleMode.Font;
      ClientSize = new Size(639, 729);
      Controls.Add(panel1);
      Controls.Add(label2);
      Controls.Add(button1);
      Controls.Add(rBtnOT);
      Controls.Add(rBtnMP);
      Controls.Add(rBtnDSQ);
      Controls.Add(rBtnDNF);
      Controls.Add(rBtnDNS);
      Controls.Add(rBtnTime);
      Controls.Add(identifierBox);
      Controls.Add(addressBox);
      Controls.Add(label1);
      Name = "Form1";
      Text = "Form1";
      FormClosing += Form1_FormClosing;
      panel1.ResumeLayout(false);
      panel1.PerformLayout();
      ResumeLayout(false);
      PerformLayout();
    }

    #endregion

    private Label label1;
    private TextBox addressBox;
    private TextBox identifierBox;
    private RadioButton rBtnTime;
    private RadioButton rBtnDNS;
    private RadioButton rBtnDNF;
    private RadioButton rBtnDSQ;
    private RadioButton rBtnMP;
    private RadioButton rBtnOT;
    private TextBox controlBox;
    private RadioButton rBtnControl;
    private RadioButton rBtnStart;
    private RadioButton rBtnFinish;
    private Button button1;
    private Label label2;
    private Panel panel1;
    private TextBox timeBox;
    private TextBox finishBox;
    private TextBox startBox;
  }
}
