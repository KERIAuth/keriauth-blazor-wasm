# Dign's Compliance with Polaris-Web Protocol

## Overview

This document summarizes conformance to the [polaris-web protocol specification](https://github.com/WebOfTrust/polaris-web), which defines a message protocol between web pages and browser extensions for KERI/ACDC operations.

## Protocol Compliance

| Polaris-Web Capability | Message Type | Supported? | Comments |
|-------------------|--------------|-----------|----------|
| **Extension Detection** | `signify-extension-client` | ✅ Yes |  |
| **Extension Ready Signal** | `signify-extension` | ✅ Yes |  |
| **Authorization (Generic)** | `/signify/authorize` | ✅ Yes | User selects an identifier or credential |
| **Authorize with AID** | `/signify/authorize/aid` | ✅ Yes | In Progress |
| **Authorize with Credential** | `/signify/authorize/credential` | ✅ Yes | In Progress |
| **Sign HTTP Request** | `/signify/sign-request` | ✅ Yes | In Progress |
| **Sign Arbitrary Data** | `/signify/sign-data` | ✅ Yes| In Progress |
| **Create Data Attestation** | `/signify/credential/create/data-attestation` | ❌ No | Planned |
| **Get Credential** | `/signify/credential/get` | ❌ No | Planned |
| **Session Management - Get Info** | `/signify/get-session-info` | ❌ No | Not implemented |
| **Session Management - Clear** | `/signify/clear-session` | ❌ No | Not implemented |
| **Vendor Configuration** | `/signify/configure-vendor` | ❌ No | Not implemented |

<br/>
<br/>

**Analysis Date:** 2026-03-10.  May include unreleased Dign features.