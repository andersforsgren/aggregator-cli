﻿using System;
using System.Collections.Generic;
using System.Text;

namespace aggregator.Engine
{
    /// <summary>
    /// Scan the top lines of a script looking for directives, whose lines start with a .
    /// </summary>
    internal class DirectivesParser
    {
        int firstCodeLine = 0;
        private readonly IAggregatorLogger logger;
        string[] ruleCode;
        List<string> references = new List<string>();
        List<string> imports = new List<string>();

        internal DirectivesParser(IAggregatorLogger logger, string[] ruleCode)
        {
            this.ruleCode = ruleCode;
            this.logger = logger;

            //defaults
            Language = Languages.Csharp;
            References = references;
            Imports = imports;
        }

        /// <summary>
        /// Grab directives
        /// </summary>
        internal bool Parse()
        {
            while (firstCodeLine < ruleCode.Length
                && ruleCode[firstCodeLine].Length > 0
                && ruleCode[firstCodeLine][0] == '.')
            {
                string directive = ruleCode[firstCodeLine].Substring(1);
                var parts = directive.Split('=');

                switch (parts[0].ToLowerInvariant())
                {
                    case "lang":
                    case "language":
                        if (parts.Length < 2)
                        {
                            logger.WriteWarning($"Unrecognized directive {directive}");
                            return false;
                        }
                        else
                        {
                            switch (parts[1].ToUpperInvariant())
                            {
                                case "C#":
                                case "CS":
                                case "CSHARP":
                                    Language = Languages.Csharp;
                                    break;
                                default:
                                    logger.WriteWarning($"Unrecognized language {parts[1]}");
                                    return false;
                            }
                        }
                        break;

                    case "r":
                    case "ref":
                    case "reference":
                        if (parts.Length < 2)
                        {
                            logger.WriteWarning($"Invalid reference directive {directive}");
                            return false;
                        }
                        else
                        {
                            references.Add(parts[1]);
                        }
                        break;

                    case "import":
                    case "imports":
                    case "namespace":
                        if (parts.Length < 2)
                        {
                            logger.WriteWarning($"Invalid import directive {directive}");
                            return false;
                        }
                        else
                        {
                            imports.Add(parts[1]);
                        }
                        break;

                    default:
                        logger.WriteWarning($"Unrecognized directive {directive}");
                        return false;
                }//switch

                firstCodeLine++;
            }//while
            return true;
        }

        internal string GetRuleCode()
        {
            return string.Join(Environment.NewLine, ruleCode, firstCodeLine, ruleCode.Length - firstCodeLine);
        }

        // directives

        internal enum Languages
        {
            Csharp
        }

        internal Languages Language { get; private set; }
        internal IReadOnlyList<string> References { get; private set; }
        internal IReadOnlyList<string> Imports { get; private set; }
    }
}
