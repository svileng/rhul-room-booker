using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.IO;

namespace RoomBooker
{
    public partial class MainForm : Form
    {
        #region Private Instance Variables
        /// <summary>Shared cookie container between login/booking requests</summary>
        private CookieContainer cookieContainer = new CookieContainer();
        /// <summary>Time to start thread</summary>
        private DateTime startingTime;
        /// <summary>Placeholder for error messages from the background thread</summary>
        private string message = string.Empty;

        #endregion

        #region Constructors
        public MainForm()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Methods

        private void BookRoom(string loginParams, string bookingParams)
        {
            string loginResponse = SendLoginRequest(loginParams);

            if (loginResponse.Contains("<authenticated>true</authenticated>"))
            {
                string bookingResponse = SendBookingRequest(bookingParams);
                if (bookingResponse.Contains("<strong>Error</strong><br/>You may not make reservations more than 2 weeks in advance.<br/>"))
                {
                    throw new Exception("Can't make reservation more than 2 weeks in advance.");
                }
                else if (bookingResponse.Contains("<strong>Error</strong><br/>Another room has been reserved during this time.<br/>You may only make 120 minutes worth of reservations per day.<br/>"))
                {
                    throw new Exception("You already booked 120 mins for that day.");
                }
                else if (bookingResponse.Contains("<strong>Error</strong><br/>Another room has been reserved during this time.<br/>"))
                {
                    throw new Exception("Room is already reserved for this time.");
                }
                else if (!bookingResponse.Contains("Your reservation has been made!"))
                {
                    throw new Exception(bookingResponse);
                }
            }
            else
            {
                throw new Exception("Authentication failed.");
            }
        }

        private string SendBookingRequest(string bookingParams)
        {
            HttpWebRequest bookRequest = (HttpWebRequest)HttpWebRequest.Create("https://libraryrooms.rhul.ac.uk/or-reserve.php");
            bookRequest.Method = "POST";
            bookRequest.ContentType = "application/x-www-form-urlencoded";
            bookRequest.CookieContainer = cookieContainer;

            byte[] requestData = Encoding.UTF8.GetBytes(bookingParams);
            using (Stream dataStream = bookRequest.GetRequestStream())
            {
                dataStream.Write(requestData, 0, requestData.Length);
            }

            HttpWebResponse bookingResponse = (HttpWebResponse)bookRequest.GetResponse();
            Stream bookingResponseStream = bookingResponse.GetResponseStream();
            StreamReader reader = new StreamReader(bookingResponseStream);
            string responseString = reader.ReadToEnd();

            return responseString;
        }

        private string SendLoginRequest(string loginParams)
        {
            try
            {
                HttpWebRequest loginRequest = (HttpWebRequest)HttpWebRequest.Create("https://libraryrooms.rhul.ac.uk/or-authenticate.php");

                loginRequest.Method = "POST";
                loginRequest.ContentType = "application/x-www-form-urlencoded";
                loginRequest.CookieContainer = cookieContainer;

                byte[] loginRequestData = Encoding.UTF8.GetBytes(loginParams);
                using (Stream dataStream = loginRequest.GetRequestStream())
                {
                    dataStream.Write(loginRequestData, 0, loginRequestData.Length);
                }

                HttpWebResponse loginResponse = (HttpWebResponse)loginRequest.GetResponse();
                Stream loginResponseStream = loginResponse.GetResponseStream();
                StreamReader reader = new StreamReader(loginResponseStream);
                string responseString = reader.ReadToEnd();

                return responseString;
            }
            catch (Exception)
            {
                throw new Exception("Login request failed (connection problem)");
            }
        }

        private void RunBackgroundWorker()
        {
            string loginParams = "username=" + txtUsername.Text + "&password=" + txtPassword.Text + "&ajax_indicator=TRUE";
            string bookingParams = "altusername=&emailconfirmation=&capacity=4&fullcapacity=8&roomid=" + GetRoomIdFor(cbRoom.Text) + "&duration=" + GetDurationFor(cbDuration.Text) + "&starttime=" + txtStartTime.Text +"&preferredname=" + txtBookingName.Text;

            backgroundWorker.RunWorkerAsync(new string[] { loginParams, bookingParams });
        }

        private string GetDurationFor(string duration)
        {
            int result;

            switch (duration)
            {
                case "1 hour":
                    result = 60;
                    break;
                case "2 hours":
                    result = 120;
                    break;
                default:
                    result = 0;
                    break;
            }

            return result.ToString();
        }

        private void OutputMessage(string msg)
        {
            txtLog.Text += DateTime.Now.ToLongTimeString() + " " + msg + "\r\n";
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private string GetRoomIdFor(string roomName)
        {
            int result;

            switch (roomName)
            {
                case "Bedford 2-01":
                    result = 29;
                    break;
                case "Bedford 2-01a":
                    result = 30;
                    break;
                case "Bedford 1-05":
                    result = 28;
                    break;
                case "Bedford 1-01":
                    result = 27;
                    break;
                case "Bedford Level 3":
                    result = 31;
                    break;
                case "Founder\'s Room 104":
                    result = 32;
                    break;
                default:
                    result = 0;
                    break;
            }
            
            return result.ToString();
        }

        #endregion

        #region Events

        private void btnRun_Click(object sender, EventArgs e)
        {
            if (btnRun.Text == "Stop")
            {
                if (backgroundWorker.IsBusy)
                {
                    backgroundWorker.CancelAsync();
                }

                timer.Stop();
                OutputMessage("System stopped by user.");

                btnRun.Text = "Run";
            }
            else
            {
                try
                {
                    OutputMessage("Starting timer...");
                    startingTime = DateTime.Parse(dateTimePicker.Text);
                    timer.Enabled = true;
                    timer.Start();

                    btnRun.Text = "Stop";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (DateTime.Compare(DateTime.Now, startingTime) >= 0)
            {
                timer.Stop();
                OutputMessage("Running automatic booker.");
                RunBackgroundWorker();
            }
        }

        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            string[] args = (string[])e.Argument;
            try
            {
                BookRoom(args[0], args[1]); // login request data and booking request data params
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                message = ex.Message;
            }
        }

        private void backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled)
            {
                Exception ex = e.Error;
                OutputMessage(message);

                if (message.Contains("weeks in advance") || message.Contains("request failed"))
                {
                    // brute-force booking until it succeeds
                    // or room is taken by someone else (tiny possibility for the latter)
                    RunBackgroundWorker();
                }
                else
                {
                    OutputMessage("Stopping automatic booker...");
                    btnRun.Text = "Run";
                }
            }
            else
            {
                OutputMessage("Room booked successfully!");
                btnRun.Text = "Run";
            }
        }

        #endregion

        private void MainForm_Load(object sender, EventArgs e)
        {
            cbRoom.SelectedIndex = 2;
            cbDuration.SelectedIndex = 1;
        }
    }
}
