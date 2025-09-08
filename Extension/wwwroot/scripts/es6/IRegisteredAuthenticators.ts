// registered-authenticators.ts
// This must be kept in sync with the model RegisteredAuthenticators.cs

import type { IRegisteredAuthenticator }  from './IRegisteredAuthenticator.js';

export interface IRegisteredAuthenticators {
    authenticators: IRegisteredAuthenticator[];
}
