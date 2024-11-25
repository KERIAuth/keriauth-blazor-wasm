// registered-authenticators.ts
// This must be kept in sync with the model RegisteredAuthenticators.cs

import { IRegisteredAuthenticator }  from "./IRegisteredAuthenticator.js";

export interface IRegisteredAuthenticators {
    authenticators: IRegisteredAuthenticator[];
}