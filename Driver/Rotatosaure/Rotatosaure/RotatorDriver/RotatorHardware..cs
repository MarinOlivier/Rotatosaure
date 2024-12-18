// TODO fill in this information for your driver, then remove this line!
//
// ASCOM Rotator hardware class for KizouRotatosaure
//
// Description:	 <To be completed by driver developer>
//
// Implements:	ASCOM Rotator interface version: <To be completed by driver developer>
// Author:		(XXX) Your N. Here <your@email.here>
//

using ASCOM;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.DeviceInterface;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Windows.Forms;

namespace ASCOM.KizouRotatosaure.Rotator
{
    //
    // TODO Replace the not implemented exceptions with code to implement the function or throw the appropriate ASCOM exception.
    //

    /// <summary>
    /// ASCOM Rotator hardware class for KizouRotatosaure.
    /// </summary>
    [HardwareClass()] // Class attribute flag this as a device hardware class that needs to be disposed by the local server when it exits.
    internal static class RotatorHardware
    {
        // Constants used for Profile persistence
        internal const string comPortProfileName = "COM Port";
        internal const string comPortDefault = "COM1";
        internal const string traceStateProfileName = "Trace Level";
        internal const string traceStateDefault = "true";

        private static string DriverProgId = ""; // ASCOM DeviceID (COM ProgID) for this driver, the value is set by the driver's class initialiser.
        private static string DriverDescription = ""; // The value is set by the driver's class initialiser.
        internal static string comPort; // COM port name (if required)
        private static bool connectedState; // Local server's connected state
        private static bool runOnce = false; // Flag to enable "one-off" activities only to run once.
        internal static Util utilities; // ASCOM Utilities object for use as required
        internal static AstroUtils astroUtilities; // ASCOM AstroUtilities object for use as required
        internal static TraceLogger tl; // Local server's trace logger object for diagnostic log with information that you specify

        private const string DEVICE_GUID = "7e2006ab-88b5-4b09-b0b3-1ac3ca8da43e";

        private const string COMMAND_PING = "COMMAND:PING";
        private const string RESULT_PING = "RESULT:PING:OK:";

        private const string COMMAND_GETANGLE = "COMMAND:ANGLE:GET";
        private const string COMMAND_SETANGLE = "COMMAND:ANGLE:SET";
        private const string COMMAND_GOTO = "COMMAND:ANGLE:GOTO";
        private const string COMMAND_CANREVERSE = "COMMAND:SETTING:CANREVERSE";
        private const string COMMAND_SETREVERSE = "COMMAND:SETTING:SETREVERSE";
        private const string COMMAND_GETREVERSE = "COMMAND:SETTING:GETREVERSE";

        // The object used to communicate with the device using serial port communication.
        private static Serial objSerial;

        // Constants used to communicate with the device
        // Make sure those values are identical to those in the Arduino Firmware.
        // (I could not come up with an easy way to share them across the two projects)
        private const string SEPARATOR = "\n";

        /// <summary>
        /// Initializes a new instance of the device Hardware class.
        /// </summary>
        static RotatorHardware()
        {
            try
            {
                // Create the hardware trace logger in the static initialiser.
                // All other initialisation should go in the InitialiseHardware method.
                tl = new TraceLogger("", "KizouRotatosaure.Hardware");

                // DriverProgId has to be set here because it used by ReadProfile to get the TraceState flag.
                DriverProgId = Rotator.DriverProgId; // Get this device's ProgID so that it can be used to read the Profile configuration values

                // ReadProfile has to go here before anything is written to the log because it loads the TraceLogger enable / disable state.
                ReadProfile(); // Read device configuration from the ASCOM Profile store, including the trace state

                LogMessage("RotatorHardware", $"Static initialiser completed.");
            }
            catch (Exception ex)
            {
                try { LogMessage("RotatorHardware", $"Initialisation exception: {ex}"); } catch { }
                MessageBox.Show($"{ex.Message}", "Exception creating ASCOM.KizouRotatosaure.Rotator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        /// <summary>
        /// Place device initialisation code here
        /// </summary>
        /// <remarks>Called every time a new instance of the driver is created.</remarks>
        internal static void InitialiseHardware()
        {
            // This method will be called every time a new ASCOM client loads your driver
            LogMessage("InitialiseHardware", $"Start.");

            // Make sure that "one off" activities are only undertaken once
            if (runOnce == false)
            {
                LogMessage("InitialiseHardware", $"Starting one-off initialisation.");

                DriverDescription = Rotator.DriverDescription; // Get this device's Chooser description

                LogMessage("InitialiseHardware", $"ProgID: {DriverProgId}, Description: {DriverDescription}");

                connectedState = false; // Initialise connected to false
                utilities = new Util(); //Initialise ASCOM Utilities object
                astroUtilities = new AstroUtils(); // Initialise ASCOM Astronomy Utilities object

                LogMessage("InitialiseHardware", "Completed basic initialisation");

                // Add your own "one off" device initialisation here e.g. validating existence of hardware and setting up communications

                LogMessage("InitialiseHardware", $"One-off initialisation complete.");
                runOnce = true; // Set the flag to ensure that this code is not run again
            }
        }

        // PUBLIC COM INTERFACE IRotatorV3 IMPLEMENTATION

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialogue form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public static void SetupDialog()
        {
            // Don't permit the setup dialogue if already connected
            if (IsConnected)
                MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm(tl))
            {
                var result = F.ShowDialog();
                if (result == DialogResult.OK)
                {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        /// <summary>Returns the list of custom action names supported by this driver.</summary>
        /// <value>An ArrayList of strings (SafeArray collection) containing the names of supported actions.</value>
        public static ArrayList SupportedActions
        {
            get
            {
                LogMessage("SupportedActions Get", "Returning empty ArrayList");
                return new ArrayList();
            }
        }

        /// <summary>Invokes the specified device-specific custom action.</summary>
        /// <param name="ActionName">A well known name agreed by interested parties that represents the action to be carried out.</param>
        /// <param name="ActionParameters">List of required parameters or an <see cref="String.Empty">Empty String</see> if none are required.</param>
        /// <returns>A string response. The meaning of returned strings is set by the driver author.
        /// <para>Suppose filter wheels start to appear with automatic wheel changers; new actions could be <c>QueryWheels</c> and <c>SelectWheel</c>. The former returning a formatted list
        /// of wheel names and the second taking a wheel name and making the change, returning appropriate values to indicate success or failure.</para>
        /// </returns>
        public static string Action(string actionName, string actionParameters)
        {
            //objSerial.ClearBuffers();
            string response = "";
            switch (actionName)
            {

                case COMMAND_GETANGLE:
                    objSerial.Transmit(COMMAND_GETANGLE + SEPARATOR);
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                    return response;

                case COMMAND_GOTO:
                    objSerial.Transmit(COMMAND_GOTO + ":" + actionParameters +  SEPARATOR);
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                    return response;
                case COMMAND_CANREVERSE:
                    objSerial.Transmit(COMMAND_CANREVERSE + SEPARATOR);
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                    return response;
                case COMMAND_GETREVERSE:
                    objSerial.Transmit(COMMAND_GETREVERSE + SEPARATOR);
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                    return response;
                case COMMAND_SETREVERSE:
                    objSerial.Transmit(COMMAND_SETREVERSE + ":" + actionParameters + SEPARATOR);
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                    return response;
                default:
                    LogMessage("Action", $"Action {actionName}, parameters {actionParameters} is not implemented");
                    throw new ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");

            }
            
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and does not wait for a response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        public static void CommandBlind(string command, bool raw)
        {
            CheckConnected("CommandBlind");
            // TODO The optional CommandBlind method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBlind must send the supplied command to the mount and return immediately without waiting for a response

            throw new MethodNotImplementedException($"CommandBlind - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a boolean response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the interpreted boolean response received from the device.
        /// </returns>
        public static bool CommandBool(string command, bool raw)
        {
            CheckConnected("CommandBool");
            // TODO The optional CommandBool method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBool must send the supplied command to the mount, wait for a response and parse this to return a True or False value

            throw new MethodNotImplementedException($"CommandBool - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a string response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the string response received from the device.
        /// </returns>
        public static string CommandString(string command, bool raw)
        {
            CheckConnected("CommandString");
            // TODO The optional CommandString method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandString must send the supplied command to the mount and wait for a response before returning this to the client

            throw new MethodNotImplementedException($"CommandString - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Deterministically release both managed and unmanaged resources that are used by this class.
        /// </summary>
        /// <remarks>
        /// TODO: Release any managed or unmanaged resources that are used in this class.
        /// 
        /// Do not call this method from the Dispose method in your driver class.
        ///
        /// This is because this hardware class is decorated with the <see cref="HardwareClassAttribute"/> attribute and this Dispose() method will be called 
        /// automatically by the  local server executable when it is irretrievably shutting down. This gives you the opportunity to release managed and unmanaged 
        /// resources in a timely fashion and avoid any time delay between local server close down and garbage collection by the .NET runtime.
        ///
        /// For the same reason, do not call the SharedResources.Dispose() method from this method. Any resources used in the static shared resources class
        /// itself should be released in the SharedResources.Dispose() method as usual. The SharedResources.Dispose() method will be called automatically 
        /// by the local server just before it shuts down.
        /// 
        /// </remarks>
        public static void Dispose()
        {
            try { LogMessage("Dispose", $"Disposing of assets and closing down."); } catch { }

            try
            {
                // Clean up the trace logger and utility objects
                tl.Enabled = false;
                tl.Dispose();
                tl = null;
            }
            catch { }

            try
            {
                utilities.Dispose();
                utilities = null;
            }
            catch { }

            try
            {
                astroUtilities.Dispose();
                astroUtilities = null;
            }
            catch { }
        }

        /// <summary>
        /// Set True to connect to the device hardware. Set False to disconnect from the device hardware.
        /// You can also read the property to check whether it is connected. This reports the current hardware state.
        /// </summary>
        /// <value><c>true</c> if connected to the hardware; otherwise, <c>false</c>.</value>
        public static bool Connected
        {
            get
            {
                LogMessage("Connected", $"Get {IsConnected}");
                return IsConnected;
            }
            set
            {
                LogMessage("Connected", $"Set {value}");
                if (value == IsConnected)
                    return;

                if (value)
                {

                    if (!System.IO.Ports.SerialPort.GetPortNames().Contains(comPort))
                    {
                        throw new InvalidValueException("Invalid COM port", comPort.ToString(), String.Join(", ", System.IO.Ports.SerialPort.GetPortNames()));
                    }

                    LogMessage("Connected Set", $"Connecting to port {comPort}");

                    objSerial = new Serial
                    {
                        Speed = SerialSpeed.ps9600,
                        PortName = comPort,
                        Connected = true
                    };

                    // Wait a second for the serial connection to establish
                    System.Threading.Thread.Sleep(2000);

                    objSerial.ClearBuffers();

                    // Poll the device (with a short timeout value) until successful,
                    // or until we've reached the retry count limit of 3...
                    objSerial.ReceiveTimeout = 1;
                    bool success = false;
                    for (int retries = 3; retries >= 0; retries--)
                    {
                        string response = "";
                        try
                        {
                            objSerial.Transmit(COMMAND_PING + SEPARATOR);
                            response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                        }
                        catch (Exception e)
                        {
                            // PortInUse or Timeout exceptions may happen here!
                            // We ignore them.
                            LogMessage("Connected Set", $"raw response {e}");
                        }

                        LogMessage("Connected Set", $"raw response {response}");

                        if (response == RESULT_PING + DEVICE_GUID)
                        {
                            success = true;
                            break;
                        }
                    }

                    if (!success)
                    {
                        objSerial.Connected = false;
                        objSerial.Dispose();
                        objSerial = null;
                        throw new ASCOM.NotConnectedException("Failed to connect");
                    }

                    // Restore default timeout value...
                    objSerial.ReceiveTimeout = 10;

                    connectedState = true;
                }
                else
                {
                    connectedState = false;

                    LogMessage("Connected Set", "Disconnecting from port {0}", comPort);

                    objSerial.Connected = false;
                    objSerial.Dispose();
                    objSerial = null;
                }
            }
        }

        /// <summary>
        /// Returns a description of the device, such as manufacturer and model number. Any ASCII characters may be used.
        /// </summary>
        /// <value>The description.</value>
        public static string Description
        {
            // TODO customise this device description if required
            get
            {
                LogMessage("Description Get", DriverDescription);
                return DriverDescription;
            }
        }

        /// <summary>
        /// Descriptive and version information about this ASCOM driver.
        /// </summary>
        public static string DriverInfo
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                // TODO customise this driver description if required
                string driverInfo = $"Information about the driver itself. Version: {version.Major}.{version.Minor}";
                LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        /// <summary>
        /// A string containing only the major and minor version of the driver formatted as 'm.n'.
        /// </summary>
        public static string DriverVersion
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = $"{version.Major}.{version.Minor}";
                LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        /// <summary>
        /// The interface version number that this device supports.
        /// </summary>
        public static short InterfaceVersion
        {
            // set by the driver wizard
            get
            {
                LogMessage("InterfaceVersion Get", "3");
                return Convert.ToInt16("3");
            }
        }

        /// <summary>
        /// The short name of the driver, for display purposes
        /// </summary>
        public static string Name
        {
            // TODO customise this device name as required
            get
            {
                string name = "Rotatosaure driver";
                LogMessage("Name Get", name);
                return name;
            }
        }

        #endregion

        #region IRotator Implementation

        private static float rotatorPosition = 0; // Synced or mechanical position angle of the rotator
        private static float mechanicalPosition = 0; // Mechanical position angle of the rotator

        /// <summary>
        /// Indicates whether the Rotator supports the <see cref="Reverse" /> method.
        /// </summary>
        /// <returns>
        /// True if the Rotator supports the <see cref="Reverse" /> method.
        /// </returns>
        internal static bool CanReverse
        {
            get
            {
                LogMessage("CanReverse Get", $"Send command: {COMMAND_CANREVERSE}");
                try
                {
                    string response = Action(COMMAND_CANREVERSE, "");
                    LogMessage("CanReverse Get", $"Receive: {response}");
                    return Convert.ToBoolean(Convert.ToInt16(response));
                }
                catch (Exception e)
                {
                    // PortInUse or Timeout exceptions may happen here!
                    // We ignore them.
                    LogMessage("CanReverse Get", $"raw response {e}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Immediately stop any Rotator motion due to a previous <see cref="Move">Move</see> or <see cref="MoveAbsolute">MoveAbsolute</see> method call.
        /// </summary>
        internal static void Halt()
        {
            LogMessage("Halt", "Not implemented");
            throw new MethodNotImplementedException("Halt");
        }

        /// <summary>
        /// Indicates whether the rotator is currently moving
        /// </summary>
        /// <returns>True if the Rotator is moving to a new position. False if the Rotator is stationary.</returns>
        internal static bool IsMoving
        {
            get
            {
                LogMessage("IsMoving Get", false.ToString()); // This rotator has instantaneous movement
                return false;
            }
        }

        /// <summary>
        /// Causes the rotator to move Position degrees relative to the current <see cref="Position" /> value.
        /// </summary>
        /// <param name="Position">Relative position to move in degrees from current <see cref="Position" />.</param>
        internal static void Move(float Position)
        {
            LogMessage("Move", Position.ToString()); // Move by this amount
            rotatorPosition += Position;
            rotatorPosition = (float)astroUtilities.Range(rotatorPosition, 0.0, true, 360.0, false); // Ensure value is in the range 0.0..359.9999...
                                                                                                     // Send the COMMAND_GETANGLE command to the serial port
            string response = Action(COMMAND_GOTO, rotatorPosition.ToString());
        }


        /// <summary>
        /// Causes the rotator to move the absolute position of <see cref="Position" /> degrees.
        /// </summary>
        /// <param name="Position">Absolute position in degrees.</param>
        internal static void MoveAbsolute(float Position)
        {
            LogMessage("MoveAbsolute", Position.ToString()); // Move to this position
            rotatorPosition = Position;
            rotatorPosition = (float)astroUtilities.Range(rotatorPosition, 0.0, true, 360.0, false); // Ensure value is in the range 0.0..359.9999...
        }

        /// <summary>
        /// Current instantaneous Rotator position, allowing for any sync offset, in degrees.
        /// </summary>
        internal static float Position
        {
            get
            {
                LogMessage("Position Get", "Requesting position"); // This rotator has instantaneous movement

                // Send the COMMAND_GETANGLE command to the serial port
                string response = Action(COMMAND_GETANGLE, "");

                LogMessage("Position Get", $"Raw response: {response}");

                // Convert the response to a float
                if (float.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                {
                    rotatorPosition = result;
                    return result;
                }
                else
                {
                    throw new InvalidOperationException("Invalid response from device");
                }   
            }
        }

        /// <summary>
        /// Sets or Returns the rotator’s Reverse state.
        /// </summary>
        internal static bool Reverse
        {
            get
            {
                LogMessage("Reverse Get", "Requesting isReverse");

                // Send the COMMAND_GETREVERSE command to the serial port
                string response = Action(COMMAND_GETREVERSE, "");

                LogMessage("Reverse Get", $"Raw response: {response}");
                return Convert.ToBoolean(Convert.ToInt16(response));
            }
            set
            {
                LogMessage("Reverse Set", $"Set {value}");
                // Send the COMMAND_GETREVERSE command to the serial port
                string response = Action(COMMAND_SETREVERSE, Convert.ToString(value));
            }
        }

        /// <summary>
        /// The minimum StepSize, in degrees.
        /// </summary>
        internal static float StepSize
        {
            get
            {
                float nbDentsEngrenageCamera = 75.0f;
                float nbPasParTourAxe = 2048.0f;

                float minStepAxe = nbPasParTourAxe / 360.0f;

                float stepSize =minStepAxe / nbDentsEngrenageCamera;
                LogMessage("StepSize", $"{stepSize}");
                return stepSize;
            }
        }

        /// <summary>
        /// The destination position angle for Move() and MoveAbsolute().
        /// </summary>
        internal static float TargetPosition
        {
            get
            {
                LogMessage("TargetPosition Get", rotatorPosition.ToString()); // This rotator has instantaneous movement
                return rotatorPosition;
            }
        }

        // IRotatorV3 methods

        /// <summary>
        /// This returns the raw mechanical position of the rotator in degrees.
        /// </summary>
        internal static float MechanicalPosition
        {
            get
            {
                LogMessage("MechanicalPosition Get", mechanicalPosition.ToString());
                return mechanicalPosition;
            }
        }

        /// <summary>
        /// Moves the rotator to the specified mechanical angle. 
        /// </summary>
        /// <param name="Position">Mechanical rotator position angle.</param>
        internal static void MoveMechanical(float Position)
        {
            LogMessage("MoveMechanical", Position.ToString()); // Move to this position

            // TODO: Implement correct sync behaviour. i.e. if the rotator has been synced the mechanical and rotator positions won't be the same
            mechanicalPosition = (float)astroUtilities.Range(Position, 0.0, true, 360.0, false); // Ensure value is in the range 0.0..359.9999...
            rotatorPosition = (float)astroUtilities.Range(Position, 0.0, true, 360.0, false); // Ensure value is in the range 0.0..359.9999...

        }

        /// <summary>
        /// Syncs the rotator to the specified position angle without moving it. 
        /// </summary>
        /// <param name="Position">Synchronised rotator position angle.</param>
        internal static void Sync(float Position)
        {
            LogMessage("Sync", Position.ToString()); // Sync to this position

            // TODO: Implement correct sync behaviour. i.e. the rotator mechanical and rotator positions may not be the same
            rotatorPosition = (float)astroUtilities.Range(Position, 0.0, true, 360.0, false); // Ensure value is in the range 0.0..359.9999...
        }

        #endregion

        #region Private properties and methods
        // Useful methods that can be used as required to help with driver development

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private static bool IsConnected
        {
            get
            {
                // TODO check that the driver hardware connection exists and is connected to the hardware
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private static void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal static void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Rotator";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(DriverProgId, traceStateProfileName, string.Empty, traceStateDefault));
                comPort = driverProfile.GetValue(DriverProgId, comPortProfileName, string.Empty, comPortDefault);
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal static void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Rotator";
                driverProfile.WriteValue(DriverProgId, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(DriverProgId, comPortProfileName, comPort.ToString());
            }
        }

        /// <summary>
        /// Log helper function that takes identifier and message strings
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        internal static void LogMessage(string identifier, string message)
        {
            tl.LogMessageCrLf(identifier, message);
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal static void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            LogMessage(identifier, msg);
        }
        #endregion
    }
}

