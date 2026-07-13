# Continuous Learning Operations

Start:

```powershell
scripts\continuous-learning\Start-ThothContinuousLearning.ps1 -RunId continuous-local
```

Status:

```powershell
scripts\continuous-learning\Get-ThothContinuousLearningStatus.ps1 -RunId continuous-local
```

Stop:

```powershell
scripts\continuous-learning\Stop-ThothContinuousLearning.ps1 -RunId continuous-local
```

Resume:

```powershell
scripts\continuous-learning\Resume-ThothContinuousLearning.ps1 -RunId continuous-local
```

Report:

```powershell
scripts\continuous-learning\Export-ThothContinuousLearningReport.ps1 -RunId continuous-local
```

Runtime state, logs, queues, and checkpoints remain under `data/continuous/`.

