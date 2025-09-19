/**
 * Browser extension permissions helper module
 * Provides typed wrappers around chrome.permissions API
 */

export interface PermissionsRequest {
    origins?: string[];
    permissions?: string[];
}

export class PermissionsHelper {
    /**
     * Check if the extension has the specified permissions
     * @param permissions The permissions to check
     * @returns Promise that resolves to true if permissions are granted
     */
    static async contains(permissions: PermissionsRequest): Promise<boolean> {
        return new Promise((resolve) => {
            chrome.permissions.contains(permissions, (hasPermission) => {
                resolve(hasPermission);
            });
        });
    }

    /**
     * Request the specified permissions from the user
     * @param permissions The permissions to request
     * @returns Promise that resolves to true if permissions are granted
     */
    static async request(permissions: PermissionsRequest): Promise<boolean> {
        return new Promise((resolve) => {
            chrome.permissions.request(permissions, (isGranted) => {
                resolve(isGranted);
            });
        });
    }

    /**
     * Remove the specified permissions
     * @param permissions The permissions to remove
     * @returns Promise that resolves to true if permissions were removed
     */
    static async remove(permissions: PermissionsRequest): Promise<boolean> {
        return new Promise((resolve) => {
            chrome.permissions.remove(permissions, (isRemoved) => {
                resolve(isRemoved);
            });
        });
    }
}

// Export for module usage
export const permissionsHelper = PermissionsHelper;