    private sealed class SectionGenerationFuture(
        ChunkGenerationStatus initialStatus)
    {
        public TaskCompletionSource<Chunk> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ChunkGenerationStatus CompletedStatus { get; set; } = initialStatus;

        public Dictionary<int, ChunkGenerationStatus> StatusWaiters { get; } = [];