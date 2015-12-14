﻿namespace Particular.ServiceInsight.Desktop.MessageProperties
{
    using System;
    using System.Collections.Generic;
    using Caliburn.Micro;
    using Framework.Rx;
    using Models;
    using Particular.ServiceInsight.Desktop.Framework.Events;
    using Particular.ServiceInsight.Desktop.Framework.MessageDecoders;
    using Particular.ServiceInsight.Desktop.MessageList;

    public abstract class HeaderInfoViewModelBase : RxScreen, IHeaderInfoViewModel
    {
        IContentDecoder<IList<HeaderInfo>> decoder;

        protected HeaderInfoViewModelBase(
            IEventAggregator eventAggregator,
            IContentDecoder<IList<HeaderInfo>> decoder,
            MessageSelectionContext selectionContext)
        {
            this.decoder = decoder;
            EventAggregator = eventAggregator;
            Selection = selectionContext;
            ConditionsMap = new Dictionary<Func<HeaderInfo, bool>, Action<HeaderInfo>>();
            MapHeaderKeys();
        }

        protected IDictionary<Func<HeaderInfo, bool>, Action<HeaderInfo>> ConditionsMap { get; private set; }

        protected IEventAggregator EventAggregator { get; }

        protected IList<HeaderInfo> Headers { get; private set; }

        protected MessageSelectionContext Selection { get; }

        public void Handle(SelectedMessageChanged @event)
        {
            ClearHeaderValues();

            if (Selection.SelectedMessage == null)
            {
                Headers = null;
            }
            else
            {
                Headers = DecodeHeader(Selection.SelectedMessage);
                OnItemsLoaded();
            }
        }

        protected virtual IList<HeaderInfo> DecodeHeader(MessageBody message)
        {
            var headers = message.HeaderRaw;
            var decodedResult = decoder.Decode(headers);

            return decodedResult.IsParsed ? decodedResult.Value : new HeaderInfo[0];
        }

        protected void OnItemsLoaded()
        {
            foreach (var condition in ConditionsMap)
            {
                foreach (var header in Headers)
                {
                    if (condition.Key(header))
                    {
                        condition.Value(header);
                    }
                }
            }

            OnMessagePropertiesLoaded();
        }

        protected virtual void OnMessagePropertiesLoaded()
        {
        }

        protected abstract void MapHeaderKeys();

        protected abstract void ClearHeaderValues();
    }
}