using System;
using System.Collections.Generic;
using System.Text;

namespace Casparcg.Core.Device
{
	public class CasparDevice
	{
        internal Casparcg.Core.Network.ServerConnection Connection { get; private set; }
        private Casparcg.Core.Network.ReconnectionHelper ReconnectionHelper { get; set; }

        public CasparDeviceSettings Settings { get; private set; }
        public List<Channel> Channels { get; private set; }
        public TemplatesCollection Templates { get; private set; }
        public List<MediaInfo> Mediafiles { get; private set; }
        public List<string> Datafiles { get; private set; }

        public string Version { get; private set; }

        public bool IsConnected { get { return (Connection == null) ? false : Connection.IsConnected; } }
        
        public event EventHandler<Casparcg.Core.Network.ConnectionEventArgs> ConnectionStatusChanged;

        public event EventHandler<DataEventArgs> DataRetrieved;
        public event EventHandler<EventArgs> UpdatedChannels;
		public event EventHandler<EventArgs> UpdatedTemplates;
		public event EventHandler<EventArgs> UpdatedMediafiles;
		public event EventHandler<EventArgs> UpdatedDatafiles;
		public event EventHandler<EventArgs> UpdatedVersion;
        public event EventHandler<DataEventArgs> InfoReceived;
        public event EventHandler<DataEventArgs> ThumbnailRetrieved;
        public event EventHandler<ResponseEventArgs> ServerResponded;

        volatile bool bIsDisconnecting = false;

		public CasparDevice()
		{
            Settings = new CasparDeviceSettings();
            Connection = new Casparcg.Core.Network.ServerConnection();
            Channels = new List<Channel>();
		    Templates = new TemplatesCollection();
		    Mediafiles = new List<MediaInfo>();
		    Datafiles = new List<string>();

            Version = "unknown";

            Connection.ProtocolStrategy = new Amcp.AMCPProtocolStrategy(this);
            Connection.ConnectionStateChanged += server__ConnectionStateChanged;
		}

        #region Server notifications
        void server__ConnectionStateChanged(object sender, Casparcg.Core.Network.ConnectionEventArgs e)
        {
            try
            {
                if (ConnectionStatusChanged != null)
                    ConnectionStatusChanged(this, e);
            }
            catch { }

            if (e.Connected)
            {
                Connection.SendString("VERSION");

                //Ask server for channels
                Connection.SendString("INFO");
            }
            else
            {
                lock (this)
                {
                    try
                    {
                        if (!bIsDisconnecting && Settings.AutoConnect)
                        {
                            Connection.ConnectionStateChanged -= server__ConnectionStateChanged;
                            ReconnectionHelper = new Casparcg.Core.Network.ReconnectionHelper(Connection, Settings.ReconnectInterval);
                            ReconnectionHelper.Reconnected += ReconnectionHelper_Reconnected;
                            ReconnectionHelper.Start();
                        }
                    }
                    catch { }
                    bIsDisconnecting = false;
                }
            }
        }

        void ReconnectionHelper_Reconnected(object sender, Casparcg.Core.Network.ConnectionEventArgs e)
        {
            lock (this)
            {
                ReconnectionHelper.Close();
                ReconnectionHelper = null;
                Connection.ConnectionStateChanged += server__ConnectionStateChanged;
            }
            server__ConnectionStateChanged(Connection, e);
        }
		#endregion

        public void SendString(string command)
        {
            if (IsConnected)
                Connection.SendString(command);
        }
		public void RefreshMediafiles()
		{
			if (IsConnected)
                Connection.SendString("CLS");
		}
		public void RefreshTemplates()
		{
			if (IsConnected)
                Connection.SendString("TLS");
		}
		public void RefreshDatalist()
		{
			if (IsConnected)
                Connection.SendString("DATA LIST");
        }
        public void StoreData(string name, ICGDataContainer data)
		{
            if (IsConnected)
                Connection.SendString(string.Format("DATA STORE \"{0}\" \"{1}\"", name, data.ToAMCPEscapedXml()));
        }
        public void RetrieveData(string name)
        {
            if (IsConnected)
                Connection.SendString(string.Format("DATA RETRIEVE \"{0}\"", name));
        }

        public void RemoveData(string name)
        {
            if (IsConnected)
                Connection.SendString(string.Format("DATA REMOVE \"{0}\"", name));
        }

        #region Connection
        public bool Connect(string host, int port)
        {
            return Connect(host, port, false);
        }

        public bool Connect(string host, int port, bool reconnect)
        {
            if (!IsConnected)
            {
                Settings.Hostname = host;
                Settings.Port = port;
                Settings.AutoConnect = reconnect;
                return Connect();
            }
            return false;
        }
        public bool Connect()
		{
			if (!IsConnected)
			{
                Connection.InitiateConnection(Settings.Hostname, Settings.Port);
				return true;
			}
			return false;
		}

		public void Disconnect()
		{
            lock (this)
            {
                bIsDisconnecting = true;
                if (ReconnectionHelper != null)
                {
                    ReconnectionHelper.Close();
                    ReconnectionHelper = null;
                    Connection.ConnectionStateChanged += server__ConnectionStateChanged;
                }
            }

            Connection.CloseConnection();
		}
        #endregion

        #region AMCP-protocol callbacks
        internal void OnUpdatedChannelInfo(List<ChannelInfo> channels)
        {
            List<Channel> newChannels = new List<Channel>();

            foreach (ChannelInfo info in channels)
            {
                if (info.ID <= Channels.Count)
                {
                    Channels[info.ID - 1].VideoMode = info.VideoMode;
                    newChannels.Add(Channels[info.ID - 1]);
                }
                else
                    newChannels.Add(new Channel(Connection, info.ID, info.VideoMode));
            }

            Channels = newChannels;
            
            if (UpdatedChannels != null)
                UpdatedChannels(this, EventArgs.Empty);
        }

        internal void OnUpdatedTemplatesList(List<TemplateInfo> templates)
		{
            TemplatesCollection newTemplates = new TemplatesCollection();
            newTemplates.Populate(templates);
            Templates = newTemplates;

			if (UpdatedTemplates != null)
				UpdatedTemplates(this, EventArgs.Empty);
		}

		internal void OnUpdatedMediafiles(List<MediaInfo> mediafiles)
		{
            Mediafiles = mediafiles;

			if (UpdatedMediafiles != null)
				UpdatedMediafiles(this, EventArgs.Empty);
		}

		internal void OnVersion(string version)
		{
			Version = version;
            OnUpdatedVersion();
		}
        
        internal void OnLoad(string clipname)
		{
        }

        internal void OnLoadBG(string clipname)
        {
        }

        internal void OnInfoReceived(string info)
        {
            InfoReceived?.Invoke(this, new DataEventArgs(info, "INFO"));
        }

        internal void OnUpdatedVersion()
        {
            UpdatedVersion?.Invoke(this, EventArgs.Empty);
        }

		internal void OnUpdatedDataList(List<string> datafiles)
		{
            Datafiles.Clear();
            Datafiles = datafiles;

			if (UpdatedDatafiles != null)
				UpdatedDatafiles(this, EventArgs.Empty);
        }

        internal void OnDataRetrieved(string data)
        {
            if(DataRetrieved != null)
                DataRetrieved(this, new DataEventArgs(data));
        }

        internal void OnThumbnailRetrieved(string data,string command)
        {
            if (ThumbnailRetrieved != null)
                ThumbnailRetrieved(this, new DataEventArgs(data,command));
        }

        internal void OnServerResponded(string command, string subcommand, List<string> data)
        {
            if (ServerResponded != null)
                ServerResponded(this, new ResponseEventArgs(command, subcommand, data));
        }
        #endregion
    }

    public class DataEventArgs : EventArgs
    {
        public DataEventArgs(string data, string command = null)
        {
            Data = data;
            Command = command;
        }

        public string Data { get; set; }
        public string Command { get; set; }
    }

    public class ResponseEventArgs : EventArgs
    {
        public ResponseEventArgs(string command, string subcommand, List<string> data)
        {
            Data = data;
            Command = command;
            Subcommand = subcommand;
        }

        public string Command { get; set; }
        public string Subcommand { get; set; }
        public List<string> Data { get; set; }
    }

    public class CasparDeviceSettings
	{
        public const int DefaultReconnectInterval = 5000;

        public CasparDeviceSettings()
        {
            ReconnectInterval = DefaultReconnectInterval;
        }
        
        public string Hostname { get; set; }
        public int Port { get; set; }
        public bool AutoConnect { get; set; }
        public int ReconnectInterval { get; set; }
    }
}
