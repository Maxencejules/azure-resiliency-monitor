import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { HealthCheckResult } from '../types';
import './Dashboard.css';

const API_URL = 'http://localhost:7071/api';

export const Dashboard: React.FC = () => {
    const [services, setServices] = useState<HealthCheckResult[]>([]);
    const [loading, setLoading] = useState(true);
    const [lastUpdate, setLastUpdate] = useState<Date>(new Date());

    const fetchHealth = async () => {
        try {
            const response = await axios.get(`${API_URL}/health/current`);
            setServices(response.data);
            setLastUpdate(new Date());
        } catch (error) {
            console.error('Failed to fetch health data:', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchHealth();
        const interval = setInterval(fetchHealth, 10000); // Refresh every 10 seconds
        return () => clearInterval(interval);
    }, []);

    const getStatusColor = (status: string) => {
        switch (status) {
            case 'Healthy': return '#10b981';
            case 'Degraded': return '#f59e0b';
            case 'Unhealthy': return '#ef4444';
            default: return '#6b7280';
        }
    };

    const healthyCount = services.filter(s => s.status === 'Healthy').length;
    const degradedCount = services.filter(s => s.status === 'Degraded').length;
    const unhealthyCount = services.filter(s => s.status === 'Unhealthy').length;

    if (loading) {
        return <div className="loading">Loading services...</div>;
    }

    return (
        <div className="dashboard">
            <header className="dashboard-header">
                <h1>🛡️ Azure Resiliency Monitor</h1>
                <span className="last-update">Last updated: {lastUpdate.toLocaleTimeString()}</span>
            </header>

            <div className="summary-cards">
                <div className="summary-card">
                    <div className="summary-value" style={{ color: '#10b981' }}>{healthyCount}</div>
                    <div className="summary-label">Healthy</div>
                </div>
                <div className="summary-card">
                    <div className="summary-value" style={{ color: '#f59e0b' }}>{degradedCount}</div>
                    <div className="summary-label">Degraded</div>
                </div>
                <div className="summary-card">
                    <div className="summary-value" style={{ color: '#ef4444' }}>{unhealthyCount}</div>
                    <div className="summary-label">Unhealthy</div>
                </div>
            </div>

            <div className="services-grid">
                {services.map((service) => (
                    <div key={service.resourceId} className="service-card">
                        <div className="service-header">
                            <h3>{service.serviceName}</h3>
                            <span
                                className="status-badge"
                                style={{ backgroundColor: getStatusColor(service.status) }}
                            >
                {service.status}
              </span>
                        </div>
                        <div className="service-details">
                            <div className="metric">
                                <span className="metric-label">Response Time:</span>
                                <span className="metric-value">
                  {Math.round(parseFloat(service.responseTime) * 1000)}ms
                </span>
                            </div>
                            {service.metadata?.cpuUsage && (
                                <div className="metric">
                                    <span className="metric-label">CPU Usage:</span>
                                    <span className="metric-value">{service.metadata.cpuUsage}%</span>
                                </div>
                            )}
                            {service.metadata?.memoryUsage && (
                                <div className="metric">
                                    <span className="metric-label">Memory Usage:</span>
                                    <span className="metric-value">{service.metadata.memoryUsage}%</span>
                                </div>
                            )}
                        </div>
                        <div className="service-message">{service.message}</div>
                    </div>
                ))}
            </div>
        </div>
    );
};