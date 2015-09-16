namespace ServiceInsight.SequenceDiagram
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Diagram;
    using Particular.ServiceInsight.Desktop.Framework;
    using Particular.ServiceInsight.Desktop.Models;

    public class ModelCreator
    {
        readonly List<ReceivedMessage> messages;
        Dictionary<Tuple<string, EndpointItem>, Handler> handlersLookup = new Dictionary<Tuple<string, EndpointItem>, Handler>();

        public ModelCreator(List<ReceivedMessage> messages)
        {
            this.messages = messages;
        }

        public List<EndpointItem> GetModel()
        {
            var messageTrees = CreateMessageTrees(messages).ToArray();

            var messagesInOrder = messageTrees.SelectMany(x => x.Walk()).ToArray();

            var registry = new EndpointRegistry();
            foreach (var message in messagesInOrder)
            {
                registry.Register(CreateSendingEndpoint(message));
            }
            foreach (var message in messagesInOrder)
            {
                registry.Register(CreateProcessingEndpoint(message));
            }

            var endpoints = new List<EndpointItem>();

            foreach (var message in messagesInOrder)
            {
                var sendingEndpoint = registry.Get(CreateSendingEndpoint(message));
                if (!endpoints.Contains(sendingEndpoint))
                {
                    endpoints.Add(sendingEndpoint);
                }

                var processingEndpoint = registry.Get(CreateProcessingEndpoint(message));
                if (!endpoints.Contains(processingEndpoint))
                {
                    endpoints.Add(processingEndpoint);
                }

                Handler sendingHandler; 
                Handler processingHandler;

                if (TryRegisterHandler(CreateSendingHandler(message, sendingEndpoint), out sendingHandler))
                {
                    sendingEndpoint.Handlers.Add(sendingHandler);
                } 
                
                if (TryRegisterHandler(CreateProcessingHandler(message, processingEndpoint), out processingHandler))
                {
                    processingEndpoint.Handlers.Add(processingHandler);
                }

                var arrow = CreateArrow(message);

                arrow.ToHandler = processingHandler;
                arrow.FromHandler = sendingHandler;

                processingHandler.In = arrow;

                sendingHandler.Out.Add(arrow);
            }

            return endpoints;
        }

        class EndpointRegistry
        {
            private IDictionary<Tuple<string, string, string>, List<EndpointItem>> store = new Dictionary<Tuple<string, string, string>, List<EndpointItem>>();

            public void Register(EndpointItem item)
            {
                List<EndpointItem> items;
                var key = MakeKey(item);
                if (!store.TryGetValue(key, out items))
                {
                    items = new List<EndpointItem>();
                    store[key] = items;
                }

                var existing = items.FirstOrDefault(x => x.Version == item.Version);
                if (existing == null)
                {
                    // Only add null if we haven't seen anything else
                    if (item.Version != null || !items.Any())
                    {
                        items.Add(item);
                    }
                }
            }

            public EndpointItem Get(EndpointItem prototype)
            {
                var key = MakeKey(prototype);

                var candidate = store[key].Where(x => x.Version != null).FirstOrDefault(x => x.Version == prototype.Version);

                if (candidate != null)
                    return candidate;

                return store[key].FirstOrDefault(x => x.Version == prototype.Version)
                       ?? store[key].FirstOrDefault();
            }

            private Tuple<string, string, string> MakeKey(EndpointItem item)
            {
                return Tuple.Create(item.FullName, item.Host, item.HostId);
            }
        }

        class MessageTreeNode
        {
            ReceivedMessage msg;
            List<MessageTreeNode> children = new List<MessageTreeNode>();
            string parent;

            public MessageTreeNode(ReceivedMessage msg)
            {
                this.msg = msg;
                parent = GetHeaderByKey(msg.headers, MessageHeaderKeys.RelatedTo, null);
            }

            public string Id
            {
                get { return msg.message_id; }
            }

            public string Parent
            {
                get { return parent; }
            }

            public void AddChild(MessageTreeNode childNode)
            {
                children.Add(childNode);
            }

            public ReceivedMessage Message
            {
                get { return msg; }
            }

            public IEnumerable<MessageTreeNode> Children
            {
                get { return children; }
            }

            public IEnumerable<ReceivedMessage> Walk()
            {
                yield return Message;
                foreach (var child in Children.OrderBy(x => x.Message.time_sent).ThenBy(x => x.Message.processed_at))
                    foreach (var walked in child.Walk())
                        yield return walked;
            }
        }

        private IEnumerable<MessageTreeNode> CreateMessageTrees(IEnumerable<ReceivedMessage> recievedMessages)
        {
            var nodes = recievedMessages.Select(x => new MessageTreeNode(x)).ToList();
            var resolved = new HashSet<MessageTreeNode>();

            var index = nodes.ToLookup(x => x.Id);

            foreach (var node in nodes)
            {
                var parent = index[node.Parent].FirstOrDefault();
                if (parent != null)
                {
                    parent.AddChild(node);
                    resolved.Add(node);
                }
            }

            return nodes.Except(resolved);
        }

        bool TryRegisterHandler(Handler newHandler, out Handler handler)
        {
            Handler existingHandler;
            var key = Tuple.Create(newHandler.ID, newHandler.Endpoint);
            if (handlersLookup.TryGetValue(key, out existingHandler))
            {
                handler = existingHandler;
                return false;
            }

            handlersLookup.Add(key, newHandler);

            handler = newHandler;
            return true;
        }

        EndpointItem CreateProcessingEndpoint(ReceivedMessage m)
        {
            return new EndpointItem(m.receiving_endpoint.name, m.receiving_endpoint.host, m.receiving_endpoint.host_id, m.sending_endpoint.Equals(m.receiving_endpoint) ? GetHeaderByKey(m.headers, MessageHeaderKeys.Version, null) : null);
        }

        EndpointItem CreateSendingEndpoint(ReceivedMessage m)
        {
            return new EndpointItem(m.sending_endpoint.name, m.sending_endpoint.host, m.sending_endpoint.host_id, GetHeaderByKey(m.headers, MessageHeaderKeys.Version, null));
        }

        Handler CreateSendingHandler(ReceivedMessage message, EndpointItem sendingEndpoint)
        {
            return new Handler(GetHeaderByKey(message.headers, MessageHeaderKeys.RelatedTo, "First"))
            {
                HandledAt = message.time_sent,
                State = HandlerState.Success,
                Endpoint = sendingEndpoint
            };
        }

        Handler CreateProcessingHandler(ReceivedMessage message, EndpointItem processingEndpoint)
        {
            var handler = new Handler(message.message_id)
            {
                HandledAt = message.processed_at,
                Name = message.message_type,
                Endpoint = processingEndpoint
            };

            if (message.invoked_sagas != null && message.invoked_sagas.Count > 0)
            {
                //TODO: Support multiple sagas!
                handler.PartOfSaga = TypeHumanizer.ToName(message.invoked_sagas[0].saga_type);
            }

            if (message.status == MessageStatus.ArchivedFailure || message.status == MessageStatus.Failed || message.status == MessageStatus.RepeatedFailure)
            {
                handler.State = HandlerState.Fail;
            }
            else
            {
                handler.State = HandlerState.Success;
            }

            return handler;
        }

        static Arrow CreateArrow(ReceivedMessage message)
        {
            var arrow = new Arrow(message.message_id)
            {
                Name = TypeHumanizer.ToName(message.message_type)
            };

            if (message.message_intent == MessageIntent.Publish)
            {
                arrow.Type = ArrowType.Event;
            }
            else
            {
                var isTimeoutString = GetHeaderByKey(message.headers, MessageHeaderKeys.IsSagaTimeout);
                var isTimeout = !string.IsNullOrEmpty(isTimeoutString) && bool.Parse(isTimeoutString);
                if (isTimeout)
                {
                    arrow.Type = ArrowType.Timeout;
                }
                else if (Equals(message.receiving_endpoint, message.sending_endpoint))
                {
                    arrow.Type = ArrowType.Local;
                }
                else
                {
                    arrow.Type = ArrowType.Command;
                }
            }

            return arrow;
        }

        static string GetHeaderByKey(IEnumerable<Header> headers, string key, string defaultValue = "")
        {
            //NOTE: Some keys start with NServiceBus, some don't
            var keyWithPrefix = "NServiceBus." + key;
            var pair = headers.FirstOrDefault(x => x.key.Equals(key, StringComparison.InvariantCultureIgnoreCase) ||
                                                   x.key.Equals(keyWithPrefix, StringComparison.InvariantCultureIgnoreCase));
            return pair == null ? defaultValue : pair.value;
        }
    }
}