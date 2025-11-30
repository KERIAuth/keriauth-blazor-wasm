// derrived from https://github.com/GLEIF-IT/vlei-trainings
import { randomPasscode, Saider, ready} from 'signify-ts';
import {
  initializeSignify, initializeAndConnectClient, createNewAID, addEndRoleForAID,
  generateOOBI, resolveOOBI, createCredentialRegistry, issueCredential,
  ipexGrantCredential, getCredentialState, waitForAndGetNotification,
  ipexAdmitGrant, markNotificationRead,
  DEFAULT_IDENTIFIER_ARGS, ROLE_AGENT, IPEX_GRANT_ROUTE, IPEX_ADMIT_ROUTE, SCHEMA_SERVER_HOST,
  prTitle, prMessage, prContinue, prAlert, isServiceHealthy, sleep
} from './utils.js';

// Re-export ready for libsodium initialization
export { ready };

// ===================== Module State =====================
let tmp: string | null = "test";

/**
 * Demo1: KERI vLEI workflow demonstration
 * Creates GLEIF, QVI, LE, and role identifiers and demonstrates credential issuance
 */
export const runDemo1 = async (): Promise<void> => {


initializeSignify()

// Create clients, AIDs and OOBIs.
prTitle("Creating clients setup")

// Fixed Bran to keep a consistent root of trust (DO NOT MODIFY or else validation with the Sally verifier will break)
const gleifBran = "Dm8Tmz05CF6_JLX9sVlFe"
const gleifAlias = 'gleif'
const { client: gleifClient } = await initializeAndConnectClient(gleifBran);
let gleifPrefix

// GLEIF GEDA (GLEIF External Delegated AID) setup
// uses try/catch to permit reusing existing GEDA upon re-run of this test file.
try{
    const gleifAid = await gleifClient.identifiers().get(gleifAlias);
    gleifPrefix = gleifAid.prefix
} catch {
    prMessage("Creating GLEIF AID")
    const { aid: newAid} = await createNewAID(gleifClient, gleifAlias, DEFAULT_IDENTIFIER_ARGS);
    await addEndRoleForAID(gleifClient, gleifAlias, ROLE_AGENT);
    gleifPrefix = newAid.i
}
const gleifOOBI = await generateOOBI(gleifClient, gleifAlias, ROLE_AGENT);

prMessage(`GLEIF Prefix: ${gleifPrefix}`)

// QVI
const qviBran = "Ao2r5MyN0s_CNWhQZUiKG"
const qviAlias = 'qvi'
const { client: qviClient } = await initializeAndConnectClient(qviBran)
const { aid: qviAid} = await createNewAID(qviClient, qviAlias, DEFAULT_IDENTIFIER_ARGS);
await addEndRoleForAID(qviClient, qviAlias, ROLE_AGENT);
const qviOOBI = await generateOOBI(qviClient, qviAlias, ROLE_AGENT);
const qviPrefix = qviAid.i
prMessage(`QVI Prefix: ${qviPrefix}`)

// LE
const leBran = qviBran
const leAlias = 'le'
const { client: leClient } = await initializeAndConnectClient(leBran)
const { aid: leAid} = await createNewAID(leClient, leAlias, DEFAULT_IDENTIFIER_ARGS);
await addEndRoleForAID(leClient, leAlias, ROLE_AGENT);
const leOOBI = await generateOOBI(leClient, leAlias, ROLE_AGENT);
const lePrefix = leAid.i
prMessage(`LE Prefix: ${lePrefix}`)

// Role Holder
const roleBran = qviBran
const roleAlias = 'role'
const { client: roleClient } = await initializeAndConnectClient(roleBran)
const { aid: roleAid} = await createNewAID(roleClient, roleAlias, DEFAULT_IDENTIFIER_ARGS);
await addEndRoleForAID(roleClient, roleAlias, ROLE_AGENT);
const roleOOBI = await generateOOBI(roleClient, roleAlias, ROLE_AGENT);
const rolePrefix = roleAid.i
prMessage(`ROLE Prefix: ${rolePrefix}`)

// Client OOBI resolution (Create contacts)
prTitle("Resolving OOBIs")

await Promise.all([
    resolveOOBI(gleifClient, qviOOBI, qviAlias),
    resolveOOBI(qviClient, gleifOOBI, gleifAlias),
    resolveOOBI(qviClient, leOOBI, leAlias),
    resolveOOBI(qviClient, roleOOBI, roleAlias),
    resolveOOBI(leClient, gleifOOBI, gleifAlias),
    resolveOOBI(leClient, qviOOBI, qviAlias),
    resolveOOBI(leClient, roleOOBI, roleAlias),
    resolveOOBI(roleClient, gleifOOBI, gleifAlias),
    resolveOOBI(roleClient, leOOBI, leAlias),
    resolveOOBI(roleClient, qviOOBI, qviAlias)
]);

// Create Credential Registries
prTitle("Creating Credential Registries")

// GLEIF GEDA Registry
// uses try/catch to permit reusing existing GEDA upon re-run of this test file.
let gleifRegistrySaid
try{
    const registries = await gleifClient.registries().list(gleifAlias);
    gleifRegistrySaid = registries[0].regk
} catch {
    prMessage("Creating GLEIF Registry")
    const { registrySaid: newRegistrySaid } = await createCredentialRegistry(gleifClient, gleifAlias, 'gleifRegistry')
    gleifRegistrySaid = newRegistrySaid
}
// QVI and LE registry
const { registrySaid: qviRegistrySaid } = await createCredentialRegistry(qviClient, qviAlias, 'qviRegistry')
const { registrySaid: leRegistrySaid } = await createCredentialRegistry(leClient, leAlias, 'leRegistry')

prContinue();
return;
}
