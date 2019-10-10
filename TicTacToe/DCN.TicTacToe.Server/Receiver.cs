﻿using DCN.TicTacToe.Shared.Enum;
using DCN.TicTacToe.Shared.ExtensionMethods;
using DCN.TicTacToe.Shared.Messages;
using DCN.TicTacToe.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DCN.TicTacToe.Server
{
    public class Receiver
    {
        private Thread receivingThread;
        private Thread sendingThread;

        #region Properties

        /// <summary>
        /// The receiver unique id.
        /// </summary>
        public Guid ID { get; set; }
        /// <summary>
        /// The reference to the parent Server.
        /// </summary>
        public Server Server { get; set; }
        /// <summary>
        /// The real TcpClient working in the background.
        /// </summary>
        public TcpClient Client { get; set; }
        /// <summary>
        /// Contains a reference to the currently in session with this receiver instance is exists.
        /// </summary>
        public Receiver OtherSideReceiver { get; set; }
        /// <summary>
        /// The current status of the reciever instance.
        /// </summary>
        public StatusEnum Status { get; set; }
        /// <summary>
        /// The message queue that contains all the messages to deliver to the remote client.
        /// </summary>
        public List<MessageBase> MessageQueue { get; private set; }
        /// <summary>
        /// The Total bytes processed by this receiver instance.
        /// </summary>
        public long TotalBytesUsage { get; set; }
        /// <summary>
        /// The Email address is used to authenticate the remote client.
        /// </summary>
        public String Email { get; set; }
        /// <summary>
        /// If true will produce and exception in some cases.
        /// </summary>
        public bool DebugMode { get; set; }
        /// <summary>
        /// The room number is used to name session with other client 
        /// </summary>
        public InGameProperties InGameProperties { get; set; }
        /// <summary>
        /// Timer to send time countdown in play game
        /// </summary>
        public CountDown CountDownInGame { get; set; }

        #endregion

        #region Constructors

        public Receiver()
        {
            ID = Guid.NewGuid();
            MessageQueue = new List<MessageBase>();
            Status = StatusEnum.Connected;

            InGameProperties = new InGameProperties();

            CountDownInGame = new CountDown();
            CountDownInGame.CoutDownEv += CountDownInGame_CoutDownEv;
        }


        /// <summary>
        /// Initializes a new reciever instance
        /// </summary>
        /// <param name="client">The TcpClient to encapsulate that was obtained by the TcpListener.</param>
        /// <param name="server">The reference to the parent server containing the receivers list.</param>
        public Receiver(TcpClient client, Server server)
            : this()
        {
            Server = server;
            Client = client;
            Client.ReceiveBufferSize = 1024;
            Client.SendBufferSize = 1024;
        }

        #endregion

        #region Event Handler

        private void CountDownInGame_CoutDownEv(int time)
        {
            if(time < 0) // no response => end game
            {
                this.CountDownInGame.Timer.Stop();
                this.CountDownInGame.ResetTimer();

                //Time out
                if (this.InGameProperties.Status == StatusInGame.InTurn)
                {
                    this.InGameProperties.Status = StatusInGame.NotReady;
                    this.SendMessage(new TimeOutRequest());
                    this.SendMessage(new StatusGameRequest(StatusGame.Lose));
                }
                else if (this.InGameProperties.Status == StatusInGame.Ready)
                {
                    this.InGameProperties.WinGame += 1;
                    this.SendMessage(new TimeOutRequest());
                    this.SendMessage(new StatusGameRequest(StatusGame.Win));
                }
                this.SendMessage(new AcceptPlayRequest());

            }
            else
            {
                UpdateCountDownRequest request = new UpdateCountDownRequest();
                request.Time = time;
                this.SendMessage(request);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initializes the receiver and start transmitting data
        /// </summary>
        public void Start()
        {
            receivingThread = new Thread(ReceivingMethod);
            receivingThread.IsBackground = true;
            receivingThread.Start();

            sendingThread = new Thread(SendingMethod);
            sendingThread.IsBackground = true;
            sendingThread.Start();
        }

        /// <summary>
        /// Stops all data transmition and disconnectes the TcpClient
        /// </summary>
        private void Disconnect()
        {
            if (Status == StatusEnum.Disconnected) return;

            if (OtherSideReceiver != null)
            {
                OtherSideReceiver.OtherSideReceiver = null;
                OtherSideReceiver.Status = StatusEnum.Validated;
                OtherSideReceiver = null;
            }

            Status = StatusEnum.Disconnected;
            Client.Client.Disconnect(false);
            Client.Close();
        }

        /// <summary>
        /// Add the specified message to the message sender queue
        /// </summary>
        /// <param name="message">The message of type MessageBase that should be added to the queue</param>
        public void SendMessage(MessageBase message)
        {
            MessageQueue.Add(message);
        }

        #endregion

        #region Threads Methods

        private void SendingMethod()
        {
            while (Status != StatusEnum.Disconnected)
            {
                if (MessageQueue.Count > 0)
                {
                    var message = MessageQueue[0];

                    try
                    {
                        BinaryFormatter f = new BinaryFormatter();
                        f.Serialize(Client.GetStream(), message);
                    }
                    catch
                    {
                        Disconnect();
                    }
                    finally
                    {
                        MessageQueue.Remove(message);
                    }
                }
                Thread.Sleep(30);
            }
        }

        private void ReceivingMethod()
        {
            while (Status != StatusEnum.Disconnected)
            {
                if (Client.Available > 0)
                {
                    TotalBytesUsage += Client.Available;

                    try
                    {
                        BinaryFormatter f = new BinaryFormatter();
                        MessageBase msg = f.Deserialize(Client.GetStream()) as MessageBase;
                        OnMessageReceived(msg);
                    }
                    catch (Exception e)
                    {
                        if (DebugMode) throw e;
                        Exception ex = new Exception("Unknown message recieved. Could not deserialize the stream.", e);
                        Debug.WriteLine(ex.Message);
                    }
                }

                Thread.Sleep(30);
            }

        }

        #endregion

        #region Message Handlers

        private void OnMessageReceived(MessageBase msg)
        {
            Type type = msg.GetType();

            if (type == typeof(ValidationRequest))
            {
                ValidationRequestHandler(msg as ValidationRequest);
            }
            else if (type == typeof(SessionRequest))
            {
                SessionRequestHandler(msg as SessionRequest);
            }
            else if (type == typeof(SessionResponse))
            {
                SessionResponseHandler(msg as SessionResponse);
            }
            else if (type == typeof(EndSessionRequest))
            {
                EndSessionRequestHandler(msg as EndSessionRequest);
            }
            else if (type == typeof(DisconnectRequest))
            {
                DisconnectRequestHandler(msg as DisconnectRequest);
            }else if(type == typeof(CreateTableRequest))
            {
                CreateTableHandler(msg as CreateTableRequest);
            }
            else if(type == typeof(TablesInProcessRequest))
            {
                ClientsInProcessRequestHandler(msg as TablesInProcessRequest);
            }
            if(type == typeof(AcceptPlayRequest))
            {
                AcceptPlayRequestHandler(msg as AcceptPlayRequest);
            }
            else if (OtherSideReceiver != null)
            {
                if(type == typeof(GameRequest))
                {
                    GameRequestHandler(msg as GameRequest);
                    
                }
                OtherSideReceiver.SendMessage(msg);
                
            }
        }

        private void GameRequestHandler(GameRequest request)
        {
            StatusGame sg = request.BoardGame.GetStatementGame();

            this.CountDownInGame.ResetTimer();
            this.OtherSideReceiver.CountDownInGame.ResetTimer();

            request.BoardGame = request.BoardGame.SwapZvO();
            this.OtherSideReceiver.SendMessage(request);

            if (sg == StatusGame.Win)
            {
                GameResponse response_1 = new GameResponse(request);
                GameResponse response_2 = new GameResponse(request);

                response_1.Game = sg;
                response_2.Game = StatusGame.Lose;

                this.SendMessage(response_1);
                this.OtherSideReceiver.SendMessage(response_2);
                this.CountDownInGame.Timer.Stop();
                this.CountDownInGame.ResetTimer();
                this.OtherSideReceiver.CountDownInGame.Timer.Stop();
                this.OtherSideReceiver.CountDownInGame.ResetTimer();

                this.SendMessage(new AcceptPlayRequest());
                this.OtherSideReceiver.SendMessage(new AcceptPlayRequest());
            }
            else if(sg == StatusGame.Tie)
            {
                GameResponse response_1 = new GameResponse(request);
                GameResponse response_2 = new GameResponse(request);

                response_1.Game = sg;
                response_2.Game = StatusGame.Tie;

                this.SendMessage(response_1);
                this.OtherSideReceiver.SendMessage(response_2);
                this.CountDownInGame.Timer.Stop();
                this.CountDownInGame.ResetTimer();
                this.OtherSideReceiver.CountDownInGame.Timer.Stop();
                this.OtherSideReceiver.CountDownInGame.ResetTimer();

                this.SendMessage(new AcceptPlayRequest());
                this.OtherSideReceiver.SendMessage(new AcceptPlayRequest());
            }
            
        }

        

        private void EndSessionRequestHandler(EndSessionRequest request)
        {
            if (OtherSideReceiver != null)
            {
                OtherSideReceiver.SendMessage(new EndSessionRequest());
                OtherSideReceiver.Status = StatusEnum.Validated;
                OtherSideReceiver.OtherSideReceiver = null;

                this.OtherSideReceiver = null;
                this.Status = StatusEnum.Validated;
                this.SendMessage(new EndSessionResponse(request));
            }
        }

        private void DisconnectRequestHandler(DisconnectRequest request)
        {
            if (OtherSideReceiver != null)
            {
                OtherSideReceiver.SendMessage(new DisconnectRequest());
                OtherSideReceiver.Status = StatusEnum.Validated;
            }

            Disconnect();
        }

        private void SessionResponseHandler(SessionResponse response)
        {
            foreach (var receiver in Server.Receivers.Where(x => x != this))
            {
                if (receiver.Email == response.Email)
                {
                    response.Email = this.Email;

                    if (response.IsConfirmed)
                    {
                        receiver.OtherSideReceiver = this;
                        this.OtherSideReceiver = receiver;
                        this.Status = StatusEnum.InSession;
                        receiver.Status = StatusEnum.InSession;

                        if (this.InGameProperties.Room == -1 && receiver.InGameProperties.Room == -1)
                        {
                            this.InGameProperties.Room = receiver.InGameProperties.Room = GetRandomTable(Server.Receivers);
                        }
                        else if (this.InGameProperties.Room == -1)
                        {
                            this.InGameProperties.Room = receiver.InGameProperties.Room;
                        }
                        else
                        {
                            receiver.InGameProperties.Room = this.InGameProperties.Room;
                        }

                    }
                    else
                    {
                        response.HasError = true;
                        response.Exception = new Exception("The session request was refused by " + response.Email);
                    }

                    receiver.SendMessage(response);
                    if(!response.HasError)
                    {
                        this.SendMessage(new AcceptPlayRequest());
                        receiver.SendMessage(new AcceptPlayRequest());
                    }
                    return;
                }
            }
        }

        private int GetRandomTable(List<Receiver> listReceiver)
        {
            Random random = new Random();
            int tableNumber = -1;

            do
            {
                tableNumber = random.Next(1000, 9999);
            } while (TableIsExists(listReceiver, tableNumber));

            return tableNumber;
        }

        private bool TableIsExists(List<Receiver> listReceiver, int tableNumber)
        {
            return (listReceiver.Where(x =>  x.InGameProperties.Room == tableNumber)).Count() > 0;
        }

        private bool IsAvaliable(StatusEnum status)
        {
            if (status == StatusEnum.Validated || status == StatusEnum.InProcess)
                return true;
            return false;
        }

        private void SessionRequestHandler(SessionRequest request)
        {
            SessionResponse response;

            if (!IsAvaliable(this.Status)) //Added after a code project user comment.
            {
                response = new SessionResponse(request);
                response.IsConfirmed = false;
                response.HasError = true;
                response.Exception = new Exception("Could not request a new session. The current client is already in session, or is not loged in.");
                SendMessage(response);
                return;
            }

            foreach (var receiver in Server.Receivers.Where(x => x != this))
            {
                if (receiver.Email == request.Email)
                {
                    if (IsAvaliable(receiver.Status))
                    {
                        request.Email = this.Email;
                        receiver.SendMessage(request);
                        return;
                    } 
                }
            }

            response = new SessionResponse(request);
            response.IsConfirmed = false;
            response.HasError = true;
            response.Exception = new Exception(request.Email + " does not exists or not loged in or in session with another user.");
            SendMessage(response);
        }

        private void ValidationRequestHandler(ValidationRequest request)
        {
            ValidationResponse response = new ValidationResponse(request);

            EventArguments.ClientValidatingEventArgs args = new EventArguments.ClientValidatingEventArgs(() =>
            {
                //Confirm Action
                Status = StatusEnum.Validated;
                Email = request.Email;
                response.IsValid = true;
                SendMessage(response);
                Server.OnClientValidated(this);
            },
            () =>
            {
                //Refuse Action
                response.IsValid = false;
                response.HasError = true;
                response.Exception = new AuthenticationException("Login failed for user " + request.Email);
                SendMessage(response);
            });

            args.Receiver = this;
            args.Request = request;

            Server.OnClientValidating(args);
        }

        private void CreateTableHandler(CreateTableRequest request)
        {
            CreateTableResponse response = new CreateTableResponse(request);

            if (request.IsCreate)
            {
                if(request.TableNumber != -1)
                {
                    if(TableIsExists(Server.Receivers, request.TableNumber))
                    {
                        response.IsSuccess = false;
                    }
                    else
                    {
                        this.InGameProperties.Room = request.TableNumber;
                        this.Status = StatusEnum.InProcess;
                        response.IsSuccess = true;
                    }
                }
                else
                {
                    this.InGameProperties.Room = GetRandomTable(Server.Receivers);
                    this.Status = StatusEnum.InProcess;
                    response.IsSuccess = true;
                }              
            }
            else
            {
                Status = StatusEnum.Validated;
                response.IsSuccess = true;
            }
            SendMessage(response);

            UpdateTableInProcessRequestHandler();
        }

        private void UpdateTableInProcessRequestHandler()
        {
            UpdateTablesInProcessRequest processRequest = new UpdateTablesInProcessRequest();
            processRequest.ClientsInProcess = GetListTableInProcess(Server.Receivers);

            foreach (var receiver in Server.Receivers.Where(x => x != this))
            {
                if (receiver.Status == StatusEnum.Validated)
                {
                    receiver.SendMessage(processRequest);
                }
            }
        }

        private List<TablePropertiesBase> GetListTableInProcess(List<Receiver> listReceiver)
        {
            List<TablePropertiesBase> listName = new List<TablePropertiesBase>();
            foreach (var receiver in Server.Receivers)
            {
                if (receiver.Status == StatusEnum.InProcess)
                {
                    listName.Add(new TablePropertiesBase(receiver.InGameProperties.Room,
                                       receiver.Email, ""));
                }
            }
            return listName;
        }

        public void ClientsInProcessRequestHandler(TablesInProcessRequest request)
        {
            TablesInProcessResponse response = new TablesInProcessResponse(request);

            response.ClientsInProcess = new List<TablePropertiesBase>();

            foreach (var receiver in Server.Receivers.Where(x => x != this))
            {
                if(receiver.Status == StatusEnum.InProcess)
                {
                    response.ClientsInProcess.Add(new TablePropertiesBase(receiver.InGameProperties.Room, 
                        receiver.Email, ""));
                }
            }

            SendMessage(response);
        }

        public void AcceptPlayRequestHandler(AcceptPlayRequest msg)
        {
            OtherSideReceiver.SendMessage(msg);
            if (msg.IsAlready)
            {
                this.InGameProperties.Status = StatusInGame.Ready;

                if (OtherSideReceiver.InGameProperties.Status == StatusInGame.Ready)
                {
                    SendDataInitGameForClients();
                }
            }
        }

        #endregion

        #region Method

        public void SendDataInitGameForClients()
        {
            Random random = new Random();

            int re = random.Next(0, 1);

            InitGame initGame = new InitGame();

            initGame.properties = this.InGameProperties;
            initGame.userName = this.Email;
            initGame.IsFirst = re == 0?false:true;
            this.InGameProperties.Status = initGame.IsFirst?StatusInGame.InTurn:StatusInGame.Ready;
            this.SendMessage(initGame);

            InitGame initGame_2 = new InitGame();
            initGame_2.properties = OtherSideReceiver.InGameProperties;
            initGame_2.userName = OtherSideReceiver.Email;
            initGame_2.IsFirst = !initGame.IsFirst;
            OtherSideReceiver.InGameProperties.Status = initGame_2.IsFirst ? StatusInGame.InTurn : StatusInGame.Ready;
            OtherSideReceiver.SendMessage(initGame_2);

            this.OtherSideReceiver.CountDownInGame.Timer.Start();
            this.CountDownInGame.Timer.Start();
        }

        #endregion
    }
}
