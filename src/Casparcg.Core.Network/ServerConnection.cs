using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace Casparcg.Core.Network
{
	public class ServerConnection
    {
        
        [Browsable(true),
        Description("Occurs when the state of the connection is changed. It is not guaranteed that this event will be fired in the main GUI-thread.")]
        public event EventHandler<ConnectionEventArgs> ConnectionStateChanged;
        
        public string Hostname { get; private set; }
        public int Port { get; private set; }
        public IProtocolStrategy ProtocolStrategy { get; set; }
		RemoteHostState RemoteState { get; set; }
       
        AsyncCallback readCallback = null;
        AsyncCallback writeCallback = null;
        AsyncCallback connectCallback = null;

        public bool IsConnected
        {
            get { return (RemoteState != null) ? RemoteState.Connected : false; }
        }


		public ServerConnection() 
		{
            readCallback = new AsyncCallback(ReadCallback);
            writeCallback = new AsyncCallback(WriteCallback);
            connectCallback = new AsyncCallback(ConnectCallback);
        }
        
        public void InitiateConnection(string hostName, int port)
        {
            if (RemoteState != null)
                CloseConnection();

			Hostname = (string.IsNullOrEmpty(hostName) ? "localhost" : hostName);
            Port = port;

			TcpClient client = new TcpClient();
			try
            {
                client.BeginConnect(Hostname, Port, connectCallback, client);
            }
            catch(Exception ex)
            {
				if (client != null)
				{
					client.Close();
					client = null;
				}
                OnClosedConnection(ex);
            }
        }

		public void CloseConnection()
		{
			//Only send notification if there actually was a connection to close
			if (DoCloseConnection())
				OnClosedConnection();
		}
        
        private void ConnectCallback(IAsyncResult ar) 
		{
			TcpClient client = null;
			try
			{
				client = (TcpClient)ar.AsyncState;
				client.EndConnect(ar);
				client.NoDelay = true;

				RemoteState = new RemoteHostState(client);
                RemoteState.GotDataToSend += RemoteState_GotDataToSend;
                RemoteState.Stream.BeginRead(RemoteState.ReadBuffer, 0, RemoteState.ReadBuffer.Length, readCallback, RemoteState);

				OnOpenedConnection();
			}
			catch(Exception ex)
			{
				if (RemoteState != null)
				{
					DoCloseConnection();
				}
				else
				{
					if (client != null)
					{
						client.Close();
						client = null;
					}
				}
				OnClosedConnection(ex);
			}
		}
        
        private void ReadCallback(IAsyncResult ar)
        {
            try
            {
                RemoteHostState state = ar.AsyncState as RemoteHostState;
                int len = 0;
                len = state.Stream.EndRead(ar);

                if (len == 0)
                    CloseConnection();
                else
                {
                    try
                    {
                        if (ProtocolStrategy != null)
                        {
							if (ProtocolStrategy.Encoding != null)
							{
                                if (state.Decoder == null)
                                    state.Decoder = ProtocolStrategy.Encoding.GetDecoder();

                                int charCount = state.Decoder.GetCharCount(state.ReadBuffer, 0, len);
								char[] chars = new char[charCount];
                                state.Decoder.GetChars(state.ReadBuffer, 0, len, chars, 0);
								string msg = new string(chars);

                                ProtocolStrategy.Parse(msg, state);
							}
							else
                                ProtocolStrategy.Parse(state.ReadBuffer, len, state);
                        }
                    }
                    catch { }

                    state.Stream.BeginRead(state.ReadBuffer, 0, state.ReadBuffer.Length, readCallback, state);
                }
            }
            catch (System.IO.IOException ioe)
            {
                if (ioe.InnerException.GetType() == typeof(System.Net.Sockets.SocketError))
                {
                    System.Net.Sockets.SocketException se = (System.Net.Sockets.SocketException)ioe.InnerException;

                    if (DoCloseConnection())
                        OnClosedConnection((se.SocketErrorCode == SocketError.Interrupted) ? null : se);
                }
                else
                    if (DoCloseConnection())
                        OnClosedConnection(ioe);
            }
            //We dont need to take care of ObjectDisposedException. 
            //ObjectDisposedException would indicate that the state has been closed, and that means it has been disconnected already
            catch { }
        }

        #region Send
        void RemoteState_GotDataToSend(object sender, EventArgs e)
        {
            DoSend();
        }
        
        void DoSend()
        {
            try
            {
                byte[] data = null;
                lock (RemoteState.SendQueue)
                {
                    if (RemoteState.SendQueue.Count > 0)
                        data = RemoteState.SendQueue.Peek();
                }

                if (data != null)
                    RemoteState.Stream.BeginWrite(data, 0, data.Length, writeCallback, RemoteState);
            }
            catch (System.IO.IOException ioe)
            {
                if (ioe.InnerException.GetType() == typeof(System.Net.Sockets.SocketError))
                {
                    System.Net.Sockets.SocketException se = (System.Net.Sockets.SocketException)ioe.InnerException;
                    if (DoCloseConnection())
                        OnClosedConnection((se.SocketErrorCode == SocketError.Interrupted) ? null : se);
                }
                else
                    if (DoCloseConnection())
                        OnClosedConnection(ioe);
            }
            //We dont need to take care of ObjectDisposedException. 
            //ObjectDisposedException would indicate that the state has been closed, and that means it has been disconnected already
            catch { }
        }
        
        void WriteCallback(IAsyncResult ar)
        {
            try
            {
                RemoteHostState state = ar.AsyncState as RemoteHostState;
                state.Stream.EndWrite(ar);
            }
            catch (System.IO.IOException ioe)
            {
                if (ioe.InnerException.GetType() == typeof(System.Net.Sockets.SocketError))
                {
                    System.Net.Sockets.SocketException se = (System.Net.Sockets.SocketException)ioe.InnerException;
                    if (DoCloseConnection())
                        OnClosedConnection((se.SocketErrorCode == SocketError.Interrupted) ? null : se);
                }
                else
                    if (DoCloseConnection())
                        OnClosedConnection(ioe);

                return;
            }
            //We dont need to take care of ObjectDisposedException. 
            //ObjectDisposedException would indicate that the state has been closed, and that means it has been disconnected already
            catch { }

            bool doSendMore = false;
            lock (RemoteState.SendQueue)
            {
                RemoteState.SendQueue.Dequeue();
                if (RemoteState.SendQueue.Count > 0)
                    doSendMore = true;
            }

            if (doSendMore)
                DoSend();
        }
        
        public void SendString(string str)
        {
            byte[] data = null;
            try
            {
                if (ProtocolStrategy != null && ProtocolStrategy.Encoding != null)
                    data = ProtocolStrategy.Encoding.GetBytes(str + ProtocolStrategy.Delimiter);
                else
                    data = Encoding.ASCII.GetBytes(str);
            }
            catch { }

            Send(data);
        }

        public void Send(byte[] data)
        {
            if(RemoteState != null)
                RemoteState.Send(data);
        }
        #endregion
        
        protected void OnOpenedConnection()
		{
            try
            {
                //Signal that we got connected
                if (ConnectionStateChanged != null)
                    ConnectionStateChanged(this, new ConnectionEventArgs(Hostname, Port, true));
            }
            catch { }
		}

		private bool DoCloseConnection()
        {
            if(RemoteState != null)
            {
                RemoteState.GotDataToSend -= RemoteState_GotDataToSend;
                RemoteState.Close();
                RemoteState = null;

                return true;
            }
            else 
                return false;
        }

        protected void OnClosedConnection()
        {
            OnClosedConnection(null);
        }
        protected void OnClosedConnection(Exception ex)
        {
            try
            {
                //Signal that we got diconnected
                if (ConnectionStateChanged != null)
                    ConnectionStateChanged(this, new ConnectionEventArgs(Hostname, Port, false, ex));
            }
            catch { }
 		}
	}
}
