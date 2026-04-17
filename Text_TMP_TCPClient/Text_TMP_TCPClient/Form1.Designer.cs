namespace Text_TMP_TCPClient
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
            button1 = new Button();
            textBox_ip = new TextBox();
            textBox_port = new TextBox();
            label1 = new Label();
            label2 = new Label();
            button2 = new Button();
            richTextBox1 = new RichTextBox();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Location = new Point(526, 226);
            button1.Name = "button1";
            button1.Size = new Size(112, 34);
            button1.TabIndex = 0;
            button1.Text = "打开";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // textBox_ip
            // 
            textBox_ip.Location = new Point(526, 167);
            textBox_ip.Name = "textBox_ip";
            textBox_ip.Size = new Size(150, 30);
            textBox_ip.TabIndex = 1;
            // 
            // textBox_port
            // 
            textBox_port.Location = new Point(526, 108);
            textBox_port.Name = "textBox_port";
            textBox_port.Size = new Size(150, 30);
            textBox_port.TabIndex = 2;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(457, 114);
            label1.Name = "label1";
            label1.Size = new Size(46, 24);
            label1.TabIndex = 3;
            label1.Text = "端口";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(457, 173);
            label2.Name = "label2";
            label2.Size = new Size(49, 24);
            label2.TabIndex = 4;
            label2.Text = "IPV4";
            // 
            // button2
            // 
            button2.Location = new Point(526, 293);
            button2.Name = "button2";
            button2.Size = new Size(112, 34);
            button2.TabIndex = 5;
            button2.Text = "发送字符串";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // richTextBox1
            // 
            richTextBox1.Location = new Point(0, 0);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(430, 327);
            richTextBox1.TabIndex = 6;
            richTextBox1.Text = "";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(11F, 24F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(richTextBox1);
            Controls.Add(button2);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(textBox_port);
            Controls.Add(textBox_ip);
            Controls.Add(button1);
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button button1;
        private TextBox textBox_ip;
        private TextBox textBox_port;
        private Label label1;
        private Label label2;
        private Button button2;
        private RichTextBox richTextBox1;
    }
}
