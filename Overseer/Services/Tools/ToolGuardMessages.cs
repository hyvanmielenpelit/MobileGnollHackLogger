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
            "Source Code service initialization in progress: GnollHack source file indexing has not finished yet (the server is generating headers with makedefs and indexing the C source in the background). Do not retry this tool in this turn. Tell the user that source file indexing has not finished yet and to ask again in a moment, as the source code data is still warming up; you may answer from general knowledge meanwhile, saying that it is not checked against the source.";

        public const string NetHackSourceCodeIndexingInProgress =
            "NetHack Source Code service initialization in progress: NetHack source file indexing has not finished yet (the server is indexing the C source in the background). Do not retry this tool in this turn. Tell the user that source file indexing has not finished yet and to ask again in a moment, as the source code data is still warming up; you may answer from general knowledge meanwhile, saying that it is not checked against the source.";
    }
}
