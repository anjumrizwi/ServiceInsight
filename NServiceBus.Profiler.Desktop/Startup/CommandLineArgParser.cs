﻿using System;
using Autofac;
using NServiceBus.Profiler.Desktop.Models;

namespace NServiceBus.Profiler.Desktop.Startup
{
    public class CommandLineArgParser : ICommandLineArgParser
    {
        private const char TokenSeparator = '&';
        private const char KeyValueSeparator = '=';

        public CommandLineOptions ParsedOptions { get; private set; }

        public void Start()
        {
            var args = Environment.GetCommandLineArgs();
            ParsedOptions = new CommandLineOptions();

            if (args.Length != 2) return;

            var parameters = args[1].ToLower();
            var tokens = parameters.Split(TokenSeparator);

            foreach (var token in tokens)
            {
                var keyValue = token.Split(KeyValueSeparator);
                if (keyValue.Length == 2)
                {
                    PopulateKeyValue(keyValue[0], keyValue[1]);
                }
                else
                {
                    ParsedOptions.SetEndpointUri(token);
                }
            }
        }

        private void PopulateKeyValue(string key, string value)
        {
            switch (key)
            {
                case "search":
                    ParsedOptions.SetSearchQuery(value);
                    break;
                case "endpointname":
                    ParsedOptions.SetEndpointName(value);
                    break;
                case "autorefresh":
                    ParsedOptions.SetAutoRefresh(value);
                    break;
                default:
                    throw new NotSupportedException(string.Format("Key {0} is not supported.", key));
            }
        }
    }

    public interface ICommandLineArgParser : IStartable
    {
        CommandLineOptions ParsedOptions { get; }
    }
}