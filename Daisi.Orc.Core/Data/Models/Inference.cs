using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    /// <summary>
    /// Represents an Inference Session.
    /// </summary>
    public class Inference
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.InferenceIdPrefix);
        /// <summary>
        /// The ID of the Account that requested this Inference Session.
        /// </summary>
        public string AccountId { get; set; }
        /// <summary>
        /// The name of the Account that requested this Inference Session.
        /// </summary>
        public string AccountName { get; set; }   

        /// <summary>
        /// The name of the Model that was requested to be used for the Inference Session.
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// The level of thought processing requested for this Inference Session.
        /// </summary>
        public ThinkLevels ThinkLevel { get; set; }

        /// <summary>
        /// Date that this Inference Session was created.
        /// </summary>
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The date that the Inference Session was closed.
        /// Null, if still active.
        /// </summary>
        public DateTime? DateClosed { get; set; }

        /// <summary>
        /// The date of the last Message that was added.
        /// </summary>
        public DateTime? DateLastMessage { get; set; }

        /// <summary>
        /// If closed, the reason that this Inference Session was closed.
        /// </summary>
        public InferenceCloseReasons? CloseReason { get; set; }

        /// <summary>
        /// The DaisiSessionId that created this Inference Session.
        /// </summary>
        public string CreatedSessionId { get; set; }

        /// <summary>
        /// The total number of tokens processed in this Inference Session.
        /// </summary>
        public int TotalTokenCount { get; set; }

        /// <summary>
        /// The total number of seconds taken to process the tokens.
        /// </summary>
        public float TokenProcessingSeconds { get; set; }

        /// <summary>
        /// The total number of tools processed in this Inference Session.
        /// </summary>
        public int TotalToolCount { get; set; }

        /// <summary>
        /// The total number of seconds to process the tools.
        /// </summary>
        public float ToolProcessingSeconds { get; set; }

        /// <summary>
        /// The list of tools that were allowed to be used in this Inference Session.
        /// </summary>
        public List<InferenceToolGroups> ToolGroups { get; set; } = new();

        /// <summary>
        /// The mesesages that were input and output in this Inference Session.
        /// </summary>
        public List<InferenceMessage> Messages { get; set; } = new();

    }

    public class InferenceMessage
    {
        public string Id { get; set; }

        /// <summary>
        /// The ID for the Inference Session that this message belongs to.
        /// </summary>
        public string InferenceId { get; set; }

        /// <summary>
        /// The DaisiSessionId used to create this Message.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// The date that this Message was created.
        /// </summary>
        public DateTime DateCreated { get; set; }

        /// <summary>
        /// The response type for this Message, if it was created by a Host.
        /// Null, if User created.
        /// </summary>
        public InferenceResponseTypes? AssistantResponseType { get; set; }

        /// <summary>
        /// The tool group that was used to create this Message.
        /// </summary>
        public InferenceToolGroups? ToolGroup { get; set; }

        /// <summary>
        /// The ID of the tool used to create this Message, if any.
        /// </summary>
        public string? ToolId { get; set; }

        /// <summary>
        /// The name of the tool used to create this Message, if any.
        /// </summary>
        public string? ToolName { get; set; }

        /// <summary>
        /// The ID of the Host that processed this Message.
        /// </summary>
        public string HostId { get; set; }

        /// <summary>
        /// The name of the Host that processed this Message.
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// The author of this Message. Should be Assistant or User.
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// String representation of the response that was given. 
        /// Will be different based on AssistantResponseType.
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// The number of tokens processed in this Message.
        /// </summary>
        public int TokenCount { get; set; }

        /// <summary>
        /// The number of seconds spent processing tokens.
        /// </summary>
        public float TokenProcessingSeconds { get; set; }

        /// <summary>
        /// The number of seconds spent processing tools.
        /// </summary>
        public float ToolProcessingSeconds { get; set;  }
     
    }
    
}
