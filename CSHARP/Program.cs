using System;
using ElectronCgi.DotNet;
using Microsoft.Extensions.Logging;
using System.IO.Ports;
using navXComUtilities;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;



namespace CSHARP
{
    class CalculatorRequest
    {
        public double Num1 { get; set; }
        public double Num2 { get; set; }
    }
    class Program
    {

        static Object bufferLock = new Object();
        static Byte[] bytes_from_usart = null;
        static int num_bytes_from_usart = 0;
        static int bytes_from_usart_offset = 0;
        static Boolean port_close_flag;
        int empty_serial_data_counter;
        string curDir;
        List<string> navxID = new List<string>();
        SerialPort port = new SerialPort();
        //bool port_close_flag = false;

        static void Main(string[] args)
        {
            var connection = new ConnectionBuilder().WithLogging(minimumLogLevel: LogLevel.Trace).Build();
            Program test = new Program();
            
            connection.On<dynamic, List<String>>("info", info => 
            {
                Console.Error.WriteLine("here1");
                test.detectCOMPort();
                return test.navxID;
            });  
            
            
            connection.On<dynamic, double>("sum", numbers =>
            {
                test.detectCOMPort();
                //Console.Error.WriteLine("here1");
                return numbers.num1 + numbers.num2;
            });
            connection.On<CalculatorRequest, double>("subtraction", numbers =>
            {
                return numbers.Num1 - numbers.Num2;
            });
            connection.On<dynamic, double>("multiplication", numbers =>
            {
                return numbers.num1 * numbers.num2;
            });
            connection.On<dynamic, double>("division", numbers => {
                if (numbers.num2 == 0){
                    Console.Error.WriteLine("Error: Division by 0");
                    throw new InvalidOperationException("Division by 0");
                }
                //when using dynamic the real type of num1 and num2 will be JValue because ElectronCGI intrenally uses Newtonsoft.Json for serialisation: https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JValue.htm
                return numbers.num1.ToObject<double>() / numbers.num2.ToObject<double>();
            });

            connection.Listen();
        }

        public void detectCOMPort()
        {
            //Console.Error.WriteLine("here2");

            SerialPort detectPort = new SerialPort();
            //SerialPort myPort = new SerialPort();
            //SerialPort myPort2 = new SerialPort();
            

            // Get a list of available COM ports
            string[] navx_port_names = navXComHelper.GetnavXSerialPortNames();
            

            // Display each port name 
            foreach (string portName in navx_port_names)
            {
                Console.Error.WriteLine(portName);
                //Console.Error.WriteLine("testtttt");
                port.PortName = portName;

                port_close_flag = false;
                port.ReadTimeout = 1000;
                port.WriteTimeout = 1000;
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                port.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                send_board_identity_request();
                Thread.Sleep(100);
                var_refresh();
                refresh_settings();
                refresh_settings();
                //send_cal_command(CAL_TYPE.CAL_TYPE_ACCEL, CAL_CMD.CAL_CMD_STATUS_REQUEST);

                //
                port_close_flag = true;
                Thread.Sleep(500);
                try{
                    port.Close();
                }
                catch (Exception){

                }
                port.Dispose();
                port.DataReceived -= new SerialDataReceivedEventHandler(DataReceivedHandler);
                empty_serial_data_counter = 0;
                bytes_from_usart = null;
                num_bytes_from_usart = 0;
                bytes_from_usart_offset = 0;

            }

            //Console.Error.WriteLine("here3");

            //return port.PortName;
        }      

        public void send_board_identity_request()
        {
            send_board_data_request(2);
        }

        private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if (!port_close_flag)
            {
                SerialPort sp = (SerialPort)sender;
                try
                {
                    lock (bufferLock)
                    {
                        int bytes_available = sp.BytesToRead;
                        Byte[] buf = new Byte[bytes_available];
                        sp.Read(buf, 0, bytes_available);
                        if (bytes_from_usart != null)
                        {
                            /* older, unprocessed data still exists. Append new data */
                            Byte[] bufexpanded = new Byte[num_bytes_from_usart + bytes_available];
                            System.Buffer.BlockCopy(bytes_from_usart, 0, bufexpanded, 0, num_bytes_from_usart);
                            System.Buffer.BlockCopy(buf, 0, bufexpanded, num_bytes_from_usart, bytes_available);
                            bytes_available = num_bytes_from_usart + bytes_available;
                            buf = bufexpanded;
                        }
                        for (int i = 0; i < bytes_available; i++)
                        {
                            if (buf[i] == Convert.ToByte('!'))
                            {
                                bytes_from_usart = buf;
                                num_bytes_from_usart = bytes_available;
                                bytes_from_usart_offset = i;
                                break;
                            }
                        }
                    }
                }
                catch (Exception) //ex
                {
                    //MessageBox.Show("DataReceivedHandler error.", "Exception:  " + ex.Message);
                }
            }
        }
        public  void send_board_data_request(int datatype)
        {
            if (port.IsOpen)
            {
                Byte[] buf = new Byte[10]; /* HACK:  Must be 9 bytes to register to the navX MXP */

                // Header
                buf[0] = Convert.ToByte('!');
                buf[1] = Convert.ToByte('#');
                buf[2] = (byte)(buf.Length - 2);
                buf[3] = Convert.ToByte('D');
                // Data
                buf[4] = (byte)datatype;
                buf[5] = 0; /* Subtype = 0 (not used) */
                // Footer
                // Checksum is at 4;
                byte checksum = (byte)0;
                for (int i = 0; i < 6; i++)
                {
                    checksum += (byte)buf[i];
                }
                CharToHex(checksum, buf, 6);

                // Terminator begins at 8;
                buf[8] = Convert.ToByte('\r');
                buf[9] = Convert.ToByte('\n');

                try
                {
                    port.Write(buf, 0, buf.Length);
                }
                catch (Exception)
                {
                }
            }
        }

        public void CharToHex(byte b, byte[] buf, int index)
        {
            String hex = b.ToString("X2");
            byte[] b2 = System.Text.Encoding.ASCII.GetBytes(hex);
            buf[index] = b2[0];
            buf[index + 1] = b2[1];
        }

        const char navx_msg_start_char = '!';
        const char navx_binary_msg_indicator = '#';
        const int navx_tuning_get_request_msg_len = 10;
        const char navx_tuning_get_request_msg_id = 'D';    /* [type],[varid] */
        const int navx_tuning_getset_msg_len = 14;
        const char navx_tuning_getset_msg_id = 'T';        /* [type],[varid],[value (16:16)]*/
        const int navx_tuning_set_response_msg_len = 11;
        const char navx_tuning_set_response_msg_id = 'v';   /* [type],[varid],[status] */
        const char navx_board_id_msg_type = 'i';
        const int navx_board_id_msg_length = 26;
        const int navx_accel_cal_status_msg_len = 15;
        const char navx_accel_cal_status_msg_type = 'c';
        
        const int tuning_var_id_unspecified = 0;
        const int tuning_var_id_motion_threshold = 1;
        const int tuning_var_id_yaw_stable_threshold = 2;
        const int tuning_var_id_mag_distrubance_threshold = 3;
        const int tuning_var_id_sea_level_pressure = 4;
        const int tuning_var_id_gyro_scale_factor_ratio = 7;
        const int tuning_var_id_max_gyro_error = 8;
        const int tuning_var_id_gyro_fsr_dps = 9;
        const int tuning_var_id_accel_fsr_g = 10;

        readonly string[] tuning_var_names = new string[]
        {
            "Linear Motion Threshold",
            "Rotation Threshold",
            "Magnetic Disturbance Threshold",
            "Sea Level Barometric Pressure",
            "Static Motion Delay",
            "Dynamic Motion Delay",
            "Gyro Scale Factor Ratio",
            "Max Gyro Error",
            "Gyro Full-scale Range",
            "Accel Full-scale Range"
        };

        readonly string[] tuning_var_recommendations = new string[]
        {
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "Consider updating the Gyro Scale Factor Ratio (on Sensor Fusion Tab)",
            "Consider re-running the Accelerometer Calibratoion (on Accelerometer Calibration Tab)"
        };


        private void floatTextToint16_buf(string float_text, Byte[] buf, int index)
        {
            short int16_val = (short)Convert.ToSingle(float_text);
            buf[index + 0] = (byte)int16_val;
            buf[index + 1] = (byte)(int16_val >> 8);
        }

        private float unsignedHudredthsToFloat(Byte[] buf, int index)
        {
            UInt16 integer = BitConverter.ToUInt16(buf, index);
            float val = integer;
            val /= 100.0f;
            return val;
        }

        private float signedHundredthsToFloat(Byte[] buf, int index)
        {
            Int16 integer = BitConverter.ToInt16(buf, index);
            float val = integer;
            val /= 100.0f;
            return val;
        }

        private float signedThousandthsToFloat(Byte[] buf, int index)
        {
            Int16 integer = BitConverter.ToInt16(buf, index);
            float val = integer;
            val /= 1000.0f;
            return val;
        }

        private float text1616FloatToFloat(Byte[] buf, int index)
        {
            Int32 integer = BitConverter.ToInt32(buf, index);
            float val = (float)integer;
            val /= 65536.0f;
            return val;
        }

        private void floatTextTo1616Float(string float_text, Byte[] buf, int index)
        {
            float x_d = Convert.ToSingle(float_text);
            x_d *= 65536.0f;
            int decimal_as_int = (int)x_d;
            if (BitConverter.IsLittleEndian)
            {
                buf[index + 3] = (byte)(decimal_as_int >> 24);
                buf[index + 2] = (byte)(decimal_as_int >> 16);
                buf[index + 1] = (byte)(decimal_as_int >> 8);
                buf[index + 0] = (byte)(decimal_as_int >> 0);
            }
            else
            {
                buf[index + 0] = (byte)(decimal_as_int >> 24);
                buf[index + 1] = (byte)(decimal_as_int >> 16);
                buf[index + 2] = (byte)(decimal_as_int >> 8);
                buf[index + 3] = (byte)(decimal_as_int >> 0);
            }
        }

        private void var_refresh()
        {
            byte[] usart_bytes;
            int usart_data_offset;
            int n_bytes_from_usart;
            /* Critical section, to prohibit reception of new bytes during the following section. */
            /* See DataReceivedHandler(). */
            lock (bufferLock)
            {
                if (bytes_from_usart == null) return;
                if (num_bytes_from_usart == 0) return;
                n_bytes_from_usart = num_bytes_from_usart;
                usart_data_offset = bytes_from_usart_offset;
                usart_bytes = new byte[n_bytes_from_usart];
                System.Buffer.BlockCopy(bytes_from_usart, 0, usart_bytes, 0, n_bytes_from_usart);
                bytes_from_usart = null;
                num_bytes_from_usart = 0;
            }
            /* End of critical section */
            try
            {
                bool device_id_msg_shown = false;
                int valid_bytes_available = n_bytes_from_usart - usart_data_offset;
                bool end_of_data = false;
                while (!end_of_data)
                {
                    if ((usart_bytes != null) && (valid_bytes_available >= 2))
                    {
                        if ((usart_bytes[usart_data_offset] == Convert.ToByte(navx_msg_start_char)) &&
                             (usart_bytes[usart_data_offset + 1] == Convert.ToByte(navx_binary_msg_indicator)))
                        {
                            /* Valid packet start found */
                            if ((usart_bytes[usart_data_offset + 2] == navx_tuning_getset_msg_len - 2) &&
                                 (usart_bytes[usart_data_offset + 3] == Convert.ToByte(navx_tuning_getset_msg_id)))
                            {
                                /* AHRS Update packet received */
                                byte[] bytes = new byte[navx_tuning_getset_msg_len];
                                System.Buffer.BlockCopy(usart_bytes, usart_data_offset, bytes, 0, navx_tuning_getset_msg_len);
                                valid_bytes_available -= navx_tuning_getset_msg_len;
                                usart_data_offset += navx_tuning_getset_msg_len;

                                byte type = bytes[4];
                                byte varid = bytes[5];
                                float value = text1616FloatToFloat(bytes, 6);
                                /*
                                if (varid == tuning_var_id_motion_threshold)
                                {
                                    maskedTextBox1.Text = String.Format("{0:##0.####}", value);
                                }
                                if (varid == tuning_var_id_yaw_stable_threshold)
                                {
                                    maskedTextBox2.Text = String.Format("{0:##0.####}", value);
                                }
                                if (varid == tuning_var_id_mag_distrubance_threshold)
                                {
                                    value *= 100; // Convert from ratio to percentage 
                                    maskedTextBox3.Text = String.Format("{0:##0.#}", value);
                                }
                                if (varid == tuning_var_id_sea_level_pressure)
                                {
                                    maskedTextBox4.Text = String.Format("{0:##0.####}", value);
                                }
                                if (varid == tuning_var_id_max_gyro_error)
                                {
                                    maxGyroErrorTextBox.Text = String.Format("{0:##0.####}", value);
                                }
                                if (varid == tuning_var_id_gyro_scale_factor_ratio)
                                {
                                    this.gyroScaleFactorRatioTextBox.Text = String.Format("{0:##0.####}", value);
                                }
                                if (varid == tuning_var_id_gyro_fsr_dps)
                                {
                                    this.gyroFullScaleRangeTextBox.Text = String.Format("{0}", value);
                                }
                                if (varid == tuning_var_id_accel_fsr_g)
                                {
                                    this.accelFullScaleRangeTextBox.Text = String.Format("{0}", value);
                                }
                                */
                            }
                            else if ((usart_bytes[usart_data_offset + 2] == navx_tuning_set_response_msg_len - 2) &&
                                     (usart_bytes[usart_data_offset + 3] == Convert.ToByte(navx_tuning_set_response_msg_id)))
                            {
                                byte[] bytes = new byte[navx_tuning_set_response_msg_len];
                                System.Buffer.BlockCopy(usart_bytes, usart_data_offset, bytes, 0, navx_tuning_set_response_msg_len);
                                valid_bytes_available -= navx_tuning_set_response_msg_len;
                                usart_data_offset += navx_tuning_set_response_msg_len;

                                byte type = bytes[4];
                                byte varid = bytes[5];
                                byte status = bytes[6];
                                if ((varid > 0) && (varid <= tuning_var_names.Length))
                                {
                                    string msg = tuning_var_names[varid - 1] + " - " + ((status == 0) ? "Success" : "Failed");
                                    if (tuning_var_recommendations[varid - 1].Length > 0)
                                    {
                                        msg += Environment.NewLine;
                                        msg += Environment.NewLine;
                                        msg += tuning_var_recommendations[varid - 1];
                                    }
                                    //MessageBox.Show(msg, "Data Set");
                                }
                            }
                            else if ((usart_bytes[usart_data_offset + 2] == navx_board_id_msg_length - 2) &&
                                     (usart_bytes[usart_data_offset + 3] == Convert.ToByte(navx_board_id_msg_type)))
                            {
                                /* Mag Cal Data Response received */
                                byte[] bytes = new byte[navx_board_id_msg_length];
                                System.Buffer.BlockCopy(usart_bytes, usart_data_offset, bytes, 0, navx_board_id_msg_length);
                                valid_bytes_available -= navx_board_id_msg_length;
                                usart_data_offset += navx_board_id_msg_length;
                                byte boardtype = bytes[4];
                                byte hwrev = bytes[5];
                                byte fw_major = bytes[6];
                                byte fw_minor = bytes[7];
                                UInt16 fw_revision = BitConverter.ToUInt16(bytes, 8);
                                byte[] unique_id = new byte[12];
                                for (int i = 0; i < 12; i++)
                                {
                                    unique_id[i] = bytes[10 + i];
                                }
                                bool show_accel_cal_and_sensor_fusion_settings = false;
                                string boardtype_string = "unknown";
                                if (hwrev == 33)
                                {
                                    boardtype_string = "navX-MXP (Classic)";
                                }
                                else if (hwrev == 34)
                                {
                                    boardtype_string = "navX2-MXP (Gen 2)";
                                    show_accel_cal_and_sensor_fusion_settings = true;
                                }
                                else if (hwrev == 40) 
                                {
                                    boardtype_string = "navX-Micro (Classic)";
                                }
                                else if (hwrev == 41)
                                {
                                    boardtype_string = "navX2-Micro (Gen 2)";
                                    show_accel_cal_and_sensor_fusion_settings = true;
                                }
                                else if ((hwrev >= 60) && (hwrev <= 69)) {
                                    if (hwrev < 62)
                                    {
                                        boardtype_string = "VMX-pi";
                                    }
                                    else
                                    {
                                        boardtype_string = "VMX2-pi";
                                        show_accel_cal_and_sensor_fusion_settings = true;
                                    }
								}
                                //updateTabPageVisibility(show_accel_cal_and_sensor_fusion_settings);
                                string msg = "Board type:  " + boardtype_string + " (" + boardtype + ")\r\n" +
                                                 "H/W Rev:  " + hwrev + "\r\n" +
                                                 "F/W Rev:  " + fw_major + "." + fw_minor + "." + fw_revision + "\r\n" +
                                                 "Unique ID:  ";
                                msg += BitConverter.ToString(unique_id);
                                navxID.Add(msg);
                                Console.Error.WriteLine(msg);
                                if (!device_id_msg_shown)
                                {
                                    //MessageBox.Show(msg, "Kauai Labs navX-Model Board ID");
                                    device_id_msg_shown = true;
                                }
                            }
                            else if ((usart_bytes[usart_data_offset + 2] == navx_accel_cal_status_msg_len - 2) &&
                                     (usart_bytes[usart_data_offset + 3] == Convert.ToByte(navx_accel_cal_status_msg_type)))
                            {
                                byte[] bytes = new byte[navx_accel_cal_status_msg_len];
                                System.Buffer.BlockCopy(usart_bytes, usart_data_offset, bytes, 0, navx_accel_cal_status_msg_len);
                                valid_bytes_available -= navx_accel_cal_status_msg_len;
                                usart_data_offset += navx_accel_cal_status_msg_len;
                                /*
                                CAL_TYPE cal_type = (CAL_TYPE)bytes[4];
                                CAL_STATE cal_state = (CAL_STATE)bytes[5];
                                CAL_QUALITY cal_qual = (CAL_QUALITY)bytes[6];
                                CAL_PARAMETER cal_param = (CAL_PARAMETER)BitConverter.ToInt32(bytes, 7);
                                accelCalStateTextBox.Text = GetCalStateDescription(cal_state);
                                accelCalQualityTextBox.Text = GetCalQualityDescription(cal_qual);
                                if (cal_type == CAL_TYPE.CAL_TYPE_ACCEL)
                                {
                                    if (last_accel_cal_cmd == CAL_CMD.CAL_CMD_START)
                                    {
                                        if (cal_param == CAL_PARAMETER.CAL_PARAM_NACK)
                                        {
                                            if (!showing_accel_cal_error)
                                            {
                                                showing_accel_cal_error = true;
                                                MessageBox.Show("Accel Calibration could not be started; verify sensor not in Startup Calibration mode", "Accel Calibration Error");
                                                showing_accel_cal_error = false;
                                            }
                                        }
                                        else if (cal_param == CAL_PARAMETER.CAL_PARAM_ACK)
                                        {
                                            enableCalControls(true);
                                        }
                                    }
                                    if ((cal_state == CAL_STATE.CAL_STATE_DONE) ||
                                        (cal_state == CAL_STATE.CAL_STATE_NONE))
                                    {
                                        enableCalControls(false);
                                    }
                                }
                                */
                            }
                            else
                            {
                                // Start of packet found, but not wanted
                                valid_bytes_available -= 1;
                                usart_data_offset += 1;
                                // Keep scanning through the remainder of the buffer
                            }
                        }
                        else
                        {
                            // Data available, but first char is not a valid start of message.
                            // Keep scanning through the remainder of the buffer
                            valid_bytes_available -= 1;
                            usart_data_offset += 1;
                        }
                    }
                    else
                    {
                        // At end of buffer, stop scanning
                        end_of_data = true;
                    }
                }
                //empty_serial_data_counter++;
                //if (empty_serial_data_counter >= 10)
                //{
                //    close_port();
                //    MessageBox.Show("No serial data.", "Warning!");
                //}
            }
            catch (Exception ex)
            {
                //close_port();
                //MessageBox.Show("Serial port error.", "Exception:  " + ex.Message);
            }
        }

        private void refresh_settings()
        {
            request_tuning_variable(tuning_var_id_motion_threshold);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_yaw_stable_threshold);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_mag_distrubance_threshold);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_sea_level_pressure);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_gyro_scale_factor_ratio);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_max_gyro_error);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_gyro_fsr_dps);
            DelayMilliseconds(25);
            var_refresh();
            request_tuning_variable(tuning_var_id_accel_fsr_g);
            DelayMilliseconds(25);
            var_refresh();
        }

        private void request_tuning_variable(int var_id)
        {
            if (port.IsOpen)
            {
                Byte[] buf = new Byte[navx_tuning_get_request_msg_len]; /* HACK:  Must be 9 bytes to register to the navX MXP */

                // Header
                buf[0] = Convert.ToByte('!');
                buf[1] = Convert.ToByte('#');
                buf[2] = (byte)(buf.Length - 2);
                buf[3] = Convert.ToByte(navx_tuning_get_request_msg_id);
                // Data
                buf[4] = (byte)0; /* Tuning Variable data type */
                buf[5] = (byte)var_id;
                // Footer
                // Checksum is at 6;
                byte checksum = (byte)0;
                for (int i = 0; i < 6; i++)
                {
                    checksum += (byte)buf[i];
                }
                CharToHex(checksum, buf, 6);

                // Terminator begins at 8;
                buf[8] = Convert.ToByte('\r');
                buf[9] = Convert.ToByte('\n');

                try
                {
                    port.Write(buf, 0, buf.Length);
                }
                catch (Exception)
                {
                }
            }
        }

        private void DelayMilliseconds(int milliseconds)
        {
            double end_ms = DateTime.Now.TimeOfDay.TotalMilliseconds + milliseconds;
            while (DateTime.Now.TimeOfDay.TotalMilliseconds < end_ms)
            {
                //System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
        }

    }
}
