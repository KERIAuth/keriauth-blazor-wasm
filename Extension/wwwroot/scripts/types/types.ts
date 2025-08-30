export interface UpdateDetails {
    reason: 'update'; // You can add other reasons if needed
    previousVersion: string;
    currentVersion: string;
    timestamp: string; // ISO 8601 formatted string
}