/**
 * Helper module for Chrome extension port message handling
 * Provides secure port message listener setup without using eval()
 */

export interface PortMessageEvent {
    type: string;
    data?: any;
}

export class PortMessageHelper {
    /**
     * Sets up message and disconnect listeners on a Chrome extension port
     * @param port The port object to listen to
     * @param connectionId Unique identifier for this connection
     * @param dotNetObjectRef Reference to the .NET object to call back
     */
    static setupPortMessageListener(
        port: chrome.runtime.Port, 
        connectionId: string, 
        dotNetObjectRef: any
    ): void {
        if (!port || !connectionId || !dotNetObjectRef) {
            console.error('PortMessageHelper: Invalid parameters provided to setupPortMessageListener');
            return;
        }

        if (!port.onMessage || !port.onDisconnect) {
            console.error('PortMessageHelper: Port object missing onMessage or onDisconnect properties');
            return;
        }

        // Set up the message listener
        const messageListener = (message: PortMessageEvent) => {
            try {
                console.debug(`PortMessageHelper: Received message on port ${connectionId}:`, message);
                
                // Call back to the .NET method with the message
                dotNetObjectRef.invokeMethodAsync('OnPortMessageReceived', connectionId, message)
                    .catch((error: Error) => {
                        console.error(`PortMessageHelper: Error calling OnPortMessageReceived:`, error);
                    });
            } catch (error) {
                console.error(`PortMessageHelper: Error in message listener for ${connectionId}:`, error);
            }
        };

        // Set up disconnect listener
        const disconnectListener = () => {
            try {
                console.debug(`PortMessageHelper: Port ${connectionId} disconnected`);
                
                // Notify .NET about the disconnection
                dotNetObjectRef.invokeMethodAsync('OnPortDisconnected', connectionId)
                    .catch((error: Error) => {
                        console.error(`PortMessageHelper: Error calling OnPortDisconnected:`, error);
                    });
            } catch (error) {
                console.error(`PortMessageHelper: Error in disconnect listener for ${connectionId}:`, error);
            }
        };

        // Attach listeners
        port.onMessage.addListener(messageListener);
        port.onDisconnect.addListener(disconnectListener);

        console.debug(`PortMessageHelper: Set up listeners for port ${connectionId}`);
    }

    /**
     * Sends a message through a specific port
     * @param port The port to send through
     * @param message The message to send
     * @returns true if successful, false otherwise
     */
    static sendPortMessage(port: chrome.runtime.Port, message: PortMessageEvent): boolean {
        if (!port || !port.postMessage) {
            console.error('PortMessageHelper: Invalid port object provided to sendPortMessage');
            return false;
        }

        try {
            port.postMessage(message);
            console.debug('PortMessageHelper: Sent message:', message);
            return true;
        } catch (error) {
            console.error('PortMessageHelper: Error sending message:', error);
            return false;
        }
    }
}