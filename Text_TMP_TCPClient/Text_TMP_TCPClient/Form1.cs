using System;
using System.Text;
using System.Windows.Forms;

namespace Text_TMP_TCPClient
{
    public partial class Form1 : Form
    {
        private TestClient testClient;
        public Form1()
        {
            InitializeComponent();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (button1.Text == "打开")
            {
                try
                {
                    button1.Enabled = false;
                    if (!string.IsNullOrEmpty(textBox_ip.Text) && !string.IsNullOrEmpty(textBox_port.Text))
                    {
                        testClient = new TestClient(int.Parse(textBox_port.Text), textBox_ip.Text);
                        testClient.InvokeUI += DataInvoke;
                        await testClient.Start();
                        if (!testClient.TcpClientDisConnected)
                        {
                            button1.Text = "关闭";
                        }
                    }
                }
                finally
                {
                    button1.Enabled = true;
                }
            }
            else
            {
                try
                {
                    button1.Enabled = false;
                    if (testClient != null)
                    {
                        await testClient.Close();
                        if (testClient.TcpClientDisConnected == true)
                            button1.Text = "打开";
                        testClient = null;
                    }
                }
                finally
                {
                    button1.Enabled = true;
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (testClient != null)
                testClient.EnqueueStrToQueue("333");
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void DataInvoke(string recvData)
        {
            if (richTextBox1.InvokeRequired)
            {
                richTextBox1.BeginInvoke(new Action<string>(DataInvoke), recvData);
                return;
            }
            richTextBox1.AppendText(recvData);
            richTextBox1.AppendText(Environment.NewLine);
        }
    }
}
