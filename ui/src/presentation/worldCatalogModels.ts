export type WorldSummaryView = {
  id: string;
  lastSavedUnixMs: number | null;
  seed: string;
  daysPassed: string;
  sphere: string;
  coordinates: string;
  compatible: boolean;
  thumbnailUrl?: string;
};
