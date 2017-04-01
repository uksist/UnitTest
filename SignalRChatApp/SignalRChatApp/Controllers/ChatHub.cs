﻿using Microsoft.AspNet.SignalR;
using SignalRChatApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SignalRChatApp.Controllers
{
    public class ChatHub : Hub
    {

        public static string emailIDLoaded = "";

        #region Connect
        public void Connect(string userName, string email)
        {
            emailIDLoaded = email;
            var id = Context.ConnectionId;
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                var item = dc.ChatUserDetails.FirstOrDefault(x => x.EmailID == email);
                if (item != null)
                {
                    dc.ChatUserDetails.Remove(item);
                    dc.SaveChanges();

                    // Disconnect
                    Clients.All.onUserDisconnectedExisting(item.ConnectionId, item.UserName);
                }

                var Users = dc.ChatUserDetails.ToList();
                if (Users.Where(x => x.EmailID == email).ToList().Count == 0)
                {
                    var userdetails = new ChatUserDetail
                    {
                        ConnectionId = id,
                        UserName = userName,
                        EmailID = email
                    };
                    dc.ChatUserDetails.Add(userdetails);
                    dc.SaveChanges();

                    // send to caller
                    var connectedUsers = dc.ChatUserDetails.ToList();
                    var CurrentMessage = dc.ChatMessageDetails.ToList();
                    Clients.Caller.onConnected(id, userName, connectedUsers, CurrentMessage);
                }

                // send to all except caller client
                Clients.AllExcept(id).onNewUserConnected(id, userName, email);
            }
        }
        #endregion

        #region Disconnect
        public override System.Threading.Tasks.Task OnDisconnected(bool stopCalled)
        {
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                var item = dc.ChatUserDetails.FirstOrDefault(x => x.ConnectionId == Context.ConnectionId);
                if (item != null)
                {
                    dc.ChatUserDetails.Remove(item);
                    dc.SaveChanges();

                    var id = Context.ConnectionId;
                    Clients.All.onUserDisconnected(id, item.UserName);
                }
            }
            return base.OnDisconnected(stopCalled);
        }
        #endregion

        #region Send_To_All
        public void SendMessageToAll(string userName, string message)
        {
            // store last 100 messages in cache
            AddAllMessageinCache(userName, message);

            // Broad cast message
            Clients.All.messageReceived(userName, message);
        }
        #endregion

        #region Private_Messages
        public void SendPrivateMessage(string toUserId, string message, string status)
        {
            string fromUserId = Context.ConnectionId;
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                var toUser = dc.ChatUserDetails.FirstOrDefault(x => x.ConnectionId == toUserId);
                var fromUser = dc.ChatUserDetails.FirstOrDefault(x => x.ConnectionId == fromUserId);
                if (toUser != null && fromUser != null)
                {
                    if (status == "Click")
                        AddPrivateMessageinCache(fromUser.EmailID, toUser.EmailID, fromUser.UserName, message);

                    // send to 
                    Clients.Client(toUserId).sendPrivateMessage(fromUserId, fromUser.UserName, message, fromUser.EmailID, toUser.EmailID, status, fromUserId);

                    // send to caller user
                    Clients.Caller.sendPrivateMessage(toUserId, fromUser.UserName, message, fromUser.EmailID, toUser.EmailID, status, fromUserId);
                }
            }
        }
        public List<PrivateChatMessage> GetPrivateMessage(string fromid, string toid, int take)
        {
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                List<PrivateChatMessage> msg = new List<PrivateChatMessage>();

                var v = (from a in dc.ChatPrivateMessageMasters
                         join b in dc.ChatPrivateMessageDetails on a.EmailID equals b.MasterEmailID into cc
                         from c in cc
                         where (c.MasterEmailID.Equals(fromid) && c.ChatToEmailID.Equals(toid)) || (c.MasterEmailID.Equals(toid) && c.ChatToEmailID.Equals(fromid))
                         orderby c.ID descending
                         select new
                         {
                             UserName = a.UserName,
                             Message = c.Message,
                             ID = c.ID
                         }).Take(take).ToList();
                v = v.OrderBy(s => s.ID).ToList();

                foreach (var a in v)
                {
                    var res = new PrivateChatMessage()
                    {
                        userName = a.UserName,
                        message = a.Message
                    };
                    msg.Add(res);
                }
                return msg;
            }
        }

        private int takeCounter = 0;
        private int skipCounter = 0;
        public List<PrivateChatMessage> GetScrollingChatData(string fromid, string toid, int start = 10, int length = 1)
        {
            takeCounter = (length * start); // 20
            skipCounter = ((length - 1) * start); // 10

            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                List<PrivateChatMessage> msg = new List<PrivateChatMessage>();
                var v = (from a in dc.ChatPrivateMessageMasters
                         join b in dc.ChatPrivateMessageDetails on a.EmailID equals b.MasterEmailID into cc
                         from c in cc
                         where (c.MasterEmailID.Equals(fromid) && c.ChatToEmailID.Equals(toid)) || (c.MasterEmailID.Equals(toid) && c.ChatToEmailID.Equals(fromid))
                         orderby c.ID descending
                         select new
                         {
                             UserName = a.UserName,
                             Message = c.Message,
                             ID = c.ID
                         }).Take(takeCounter).Skip(skipCounter).ToList();

                foreach (var a in v)
                {
                    var res = new PrivateChatMessage()
                    {
                        userName = a.UserName,
                        message = a.Message
                    };
                    msg.Add(res);
                }
                return msg;
            }
        }
        #endregion

        #region Save_Cache
        private void AddAllMessageinCache(string userName, string message)
        {
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                var messageDetail = new ChatMessageDetail
                {
                    UserName = userName,
                    Message = message,
                    EmailID = emailIDLoaded
                };
                dc.ChatMessageDetails.Add(messageDetail);
                dc.SaveChanges();
            }
        }

        private void AddPrivateMessageinCache(string fromEmail, string chatToEmail, string userName, string message)
        {
            using (ChatAppDBEntities dc = new ChatAppDBEntities())
            {
                // Save master
                var master = dc.ChatPrivateMessageMasters.ToList().Where(a => a.EmailID.Equals(fromEmail)).ToList();
                if (master.Count == 0)
                {
                    var result = new ChatPrivateMessageMaster
                    {
                        EmailID = fromEmail,
                        UserName = userName
                    };
                    dc.ChatPrivateMessageMasters.Add(result);
                    dc.SaveChanges();
                }

                // Save details
                var resultDetails = new ChatPrivateMessageDetail
                {
                    MasterEmailID = fromEmail,
                    ChatToEmailID = chatToEmail,
                    Message = message
                };
                dc.ChatPrivateMessageDetails.Add(resultDetails);
                dc.SaveChanges();
            }
        }
        #endregion

    }
    public class PrivateChatMessage
    {
        public string userName { get; set; }
        public string message { get; set; }
    }
}