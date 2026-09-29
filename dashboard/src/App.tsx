import { Dashboard } from './components/Dashboard';
import { getDemoScenario } from './data/demo';
import './App.css';

function App() {
    const demoScenario = getDemoScenario(new URLSearchParams(window.location.search).get('demo'));
    return <Dashboard demoScenario={demoScenario} />;
}

export default App;
