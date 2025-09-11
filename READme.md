# Azure Resiliency Monitor

A full-stack monitoring solution for Azure services with automated health checks, recovery actions, and real-time dashboard visualization.

## 🎯 Overview

This project demonstrates enterprise-level monitoring patterns for Azure services, featuring automatic recovery capabilities and a responsive real-time dashboard. Built with configuration-driven design principles, it supports both mock services for development and real Azure resources for production.

## ✨ Features

- **Real-time Health Monitoring** - Automated health checks every minute
- **Auto-Recovery Actions** - Automatic service restart for unhealthy resources
- **Live Dashboard** - React-based dashboard with 10-second auto-refresh
- **Mock Data Mode** - Built-in mock services for cost-free development and testing
- **Configuration-Driven** - Environment-based resource configuration
- **Extensible Architecture** - Easy to add new service monitors
- **REST API** - HTTP endpoints for health data access
- **Responsive UI** - Mobile-friendly dashboard design

## 🛠️ Tech Stack

### Backend
- **.NET 8** - Latest framework with isolated process model
- **Azure Functions** - Serverless compute for monitoring logic
- **Azure SDK** - ARM client libraries for resource management
- **C# 12** - Modern language features

### Frontend
- **React 18** - UI framework with TypeScript
- **Axios** - HTTP client for API calls
- **Recharts** - Data visualization (ready for charts)
- **CSS3** - Custom styling with responsive design

### Azure Services Supported
- App Services
- Cosmos DB
- Service Bus
- Storage Accounts
- Function Apps

## 🚀 Getting Started

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- Azure Functions Core Tools v4
- Azure subscription (optional - can run with mock data)

### Installation

1. **Clone the repository**
```bash
git clone https://github.com/Maxencejules/azure-resiliency-monitor.git
cd azure-resiliency-monitor
```

2. **Install backend dependencies**
```bash
dotnet restore
dotnet build
```

3. **Install frontend dependencies**
```bash
cd dashboard
npm install
```

### Running Locally

1. **Start Azurite (Storage Emulator)**
```bash
azurite
```

2. **Start Azure Functions** (new terminal)
```bash
cd AzureResiliencyMonitor.Functions
func start
```

3. **Start React Dashboard** (new terminal)
```bash
cd dashboard
npm start
```

4. **Open browser**
- Dashboard: http://localhost:3000
- API Health: http://localhost:7071/api/health
- Current Status: http://localhost:7071/api/health/current

## 🏗️ Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│                 │     │                 │     │                 │
│  React Dashboard│────▶│  Azure Functions│────▶│  Azure Resources│
│                 │     │                 │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
        │                       │                         │
        │                       │                         │
        ▼                       ▼                         ▼
   [Browser UI]          [Timer Trigger]           [ARM API/Mock]
                         [HTTP Triggers]
```

### Key Components

- **HealthCheckService** - Orchestrates all monitoring operations
- **ServiceMonitors** - Individual monitors for each Azure service type
- **MockServiceMonitor** - Generates test data for development
- **Timer Function** - Triggers health checks every minute
- **API Functions** - HTTP endpoints for dashboard access

## 📊 Monitoring Capabilities

| Service Type | Health Check | Auto-Recovery | Metrics |
|-------------|--------------|---------------|---------|
| App Service | ✅ | ✅ Restart | CPU, Memory, RPS |
| Cosmos DB | ✅ | ✅ Failover Ready | Throughput, Latency |
| Service Bus | 🔄 | 🔄 | Queue Length |
| Storage | 🔄 | 🔄 | IOPS, Bandwidth |

✅ Implemented | 🔄 Ready for Extension

## 🔧 Configuration

### Environment Variables (local.settings.json)
```json
{
  "Values": {
    "AZURE_FUNCTIONS_ENVIRONMENT": "Development",
    "MonitoredResources": "resource1,resource2",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true"
  }
}
```

### Adding Real Azure Resources
```json
"MonitoredResources": "/subscriptions/{id}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{name}"
```

## 🧪 Development Mode

The project includes a mock service monitor that generates realistic test data:
- Random health statuses (Healthy, Degraded, Unhealthy)
- Simulated CPU and memory metrics
- Variable response times
- No Azure costs

## 📈 API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Basic health check |
| `/api/health/current` | GET | Get current status of all monitored services |

### Sample API Response
```json
{
  "serviceName": "app-1",
  "resourceId": "mock://subscription/test-sub/resourceGroups/test-rg/providers/Microsoft.Web/sites/app-1",
  "serviceType": "AppService",
  "status": "Healthy",
  "message": "Mock status: Healthy",
  "checkedAt": "2025-09-11T06:49:25.2142842Z",
  "responseTime": "00:00:00.1105291",
  "metadata": {
    "mock": true,
    "cpuUsage": 45,
    "memoryUsage": 39,
    "requestsPerSecond": 842
  }
}
```

## 🧩 Project Structure

```
azure-resiliency-monitor/
├── AzureResiliencyMonitor.Core/          # Business logic and models
│   ├── Interfaces/                       # Service contracts
│   ├── Models/                           # Data models
│   ├── Monitors/                         # Service-specific monitors
│   └── Services/                         # Core services
├── AzureResiliencyMonitor.Functions/     # Azure Functions
│   ├── HealthCheckFunction.cs            # Timer trigger
│   ├── HealthCheckApiFunction.cs         # HTTP endpoints
│   └── Program.cs                        # DI configuration
├── AzureResiliencyMonitor.Tests/         # Unit tests
├── dashboard/                             # React frontend
│   ├── src/
│   │   ├── components/                   # React components
│   │   └── types/                        # TypeScript definitions
│   └── package.json
└── AzureResiliencyMonitor.sln            # Solution file
```

## 🚢 Deployment

### Azure Functions Deployment
```bash
func azure functionapp publish <function-app-name>
```

### Dashboard Deployment (Azure Static Web Apps)
```bash
cd dashboard
npm run build
# Deploy build folder to Azure Static Web Apps
```

## 🧪 Testing

### Backend Tests
```bash
dotnet test
```

### Frontend Tests
```bash
cd dashboard
npm test
```

## 📝 Future Enhancements

- [ ] Add SignalR for real-time WebSocket updates
- [ ] Implement data persistence with Cosmos DB
- [ ] Add email/Teams notifications for critical alerts
- [ ] Create Terraform templates for infrastructure
- [ ] Add Application Insights integration
- [ ] Implement custom alert rules
- [ ] Add historical trending charts
- [ ] Support for more Azure service types
- [ ] Add authentication with Azure AD
- [ ] Implement role-based access control
- [ ] Add export functionality for reports
- [ ] Create mobile app version

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 👤 Author

**Maxence Jules**
- GitHub: Maxencejules(https://github.com/Maxencejules)
- LinkedIn: Maxence Jules (https://linkedin.com/in/julesmax)
- Email: Powe840@gmail.com

## 🙏 Acknowledgments

- Built as a portfolio project to demonstrate Azure monitoring capabilities
- Designed following Microsoft best practices for cloud-native applications
- Implements enterprise patterns for production readiness

## 📞 Support

For support, email powe840@gmail.com or open an issue in the GitHub repository.

---

**Note**: This is a demonstration project using mock data for development. For production use, configure with real Azure resource IDs and appropriate authentication.