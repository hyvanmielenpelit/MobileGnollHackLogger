namespace Overseer.Services.Tools
{
    public static class ToolGuardMessages
    {
        public const string WikiIndexingInProgress =
            "GnollHack Wiki service initialization in progress: The GnollHack wiki index is currently being built in the background and is not yet available for queries. Do not retry this tool in this turn. Please inform the user that GnollHack wiki data is warming up, or answer using your general knowledge without this tool.";

        public const string NetHackWikiIndexingInProgress =
            "NetHack Wiki service initialization in progress: The NetHack wiki index is currently being built in the background and is not yet available for queries. Do not retry this tool in this turn. Please inform the user that NetHack wiki data is warming up, or answer using your general knowledge without this tool.";

        public const string KnowledgeBaseIndexingInProgress =
            "Knowledge Base service initialization in progress: The Knowledge Base guides are currently being loaded in the background and are not yet available for queries. Do not retry this tool in this turn. Please inform the user that Knowledge Base data is warming up, or answer using your general knowledge without this tool.";

        public const string SourceCodeIndexingInProgress =
            "Source Code service initialization in progress: The C source code repository is currently being indexed in the background and is not yet available for queries. Do not retry this tool in this turn. Please inform the user that source code data is warming up, or answer using your general knowledge without this tool.";
    }
}
