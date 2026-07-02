# TeslaVinInfo

TeslaVinInfo is a .NET 8 console application that calls the official Tesla Fleet API for a specific VIN.

It supports:

- Basic vehicle info: `GET /api/1/vehicles/{vin}`
- Live vehicle data: `GET /api/1/vehicles/{vin}/vehicle_data`

The app never stores or hardcodes your VIN, access token, client secret, refresh token, or other private data.

## Prerequisites

- .NET 8 SDK
- A Tesla Fleet API OAuth access token
- Access to the vehicle for the Tesla account represented by the token

## Create a Tesla Access Token

Create a Tesla Fleet API OAuth access token through your Tesla developer application and OAuth flow. The exact flow depends on how your Tesla developer app is configured, but at a high level:

1. Create or use an existing Tesla developer application.
2. Configure the required Fleet API scopes for the vehicle data you want to read.
3. Complete the OAuth authorization flow for the Tesla account that owns or has access to the vehicle.
4. Exchange the authorization code for an access token.
5. Set the access token in the `TESLA_ACCESS_TOKEN` environment variable.

Do not commit access tokens, refresh tokens, client secrets, or private keys to source control.

## Environment Variables

`TESLA_ACCESS_TOKEN` is required.

`TESLA_API_BASE` is optional. If it is not set, the app uses:

```text
https://fleet-api.prd.eu.vn.cloud.tesla.com
```

### macOS/Linux

```bash
export TESLA_ACCESS_TOKEN="your_access_token_here"
export TESLA_API_BASE="https://fleet-api.prd.eu.vn.cloud.tesla.com"
```

If you want to use the default base URL, omit `TESLA_API_BASE`:

```bash
export TESLA_ACCESS_TOKEN="your_access_token_here"
```

### Windows PowerShell

```powershell
$env:TESLA_ACCESS_TOKEN = "your_access_token_here"
$env:TESLA_API_BASE = "https://fleet-api.prd.eu.vn.cloud.tesla.com"
```

If you want to use the default base URL, omit `TESLA_API_BASE`:

```powershell
$env:TESLA_ACCESS_TOKEN = "your_access_token_here"
```

## Run

From the project folder:

```bash
dotnet run -- 5YJ3E1EC6LF784505
```

Request live vehicle data:

```bash
dotnet run -- 5YJ3E1EC6LF784505 --live
```

Print raw formatted JSON for basic vehicle info:

```bash
dotnet run -- 5YJ3E1EC6LF784505 --raw
```

Print parsed live data and raw formatted JSON:

```bash
dotnet run -- 5YJ3E1EC6LF784505 --live --raw
```

## Notes

The `vehicle_data` endpoint wakes or queries live vehicle systems and should not be polled frequently. Use it for occasional reads.

Common failures include expired tokens, missing vehicle access, an incorrect VIN, and vehicles that are offline or asleep.

## Tesla Fleet App Registration

This repository also hosts the Tesla Fleet API public key for Partner Account registration through GitHub Pages.

Use these values in the Tesla Developer application:

```text
Allowed Origin URL(s):
https://lashamorgoshia.github.io

Allowed Redirect URI(s):
http://localhost:8931/callback

Allowed Returned URL(s):
http://localhost:8931/callback
```

The public key must be available at:

```text
https://lashamorgoshia.github.io/.well-known/appspecific/com.tesla.3p.public-key.pem
```

When calling the Partner Account register endpoint, use this domain value without `https://`:

```text
lashamorgoshia.github.io
```

Keep `tesla-private-key.pem` local and private. Do not commit it or upload it to GitHub.
