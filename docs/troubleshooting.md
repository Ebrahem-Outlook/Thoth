# Troubleshooting

## Missing .NET SDK

Install .NET SDK 8.x and verify:

```powershell
dotnet --info
```

## npm ci Fails

Use a Node.js version supported by Angular 21 and npm 10.x or newer:

```powershell
node --version
npm --version
npm ci --prefix src/Thoth.Web
```

## Frontend Cannot Reach API

Check `src/Thoth.Web/src/environments/environment.ts` and run:

```powershell
dotnet run --project src/Thoth.Api --urls http://127.0.0.1:5055
```

The API CORS policy allows localhost and 127.0.0.1 origins.

## Missing or Unqualified Checkpoint

Run:

```powershell
dotnet run --project src/Thoth.Cli -- model-status
```

If status is `Missing`, train or point to the correct checkpoint. If status is `Unqualified`, run evaluation and inspect the sidecar metadata.

## Native TorchSharp Setup

The current CPU Transformer foundation does not require TorchSharp. If future work adds TorchSharp/CUDA, keep native package setup separate from the normal lightweight CPU correctness suite.

