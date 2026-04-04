import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './lib/auth';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Dashboard } from './pages/Dashboard';
import { SessionViewer } from './pages/SessionViewer';
import { LoginPage } from './pages/LoginPage';
import { OrgPage } from './pages/OrgPage';

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<ProtectedRoute><Dashboard /></ProtectedRoute>} />
          <Route path="/session/:id" element={<ProtectedRoute><SessionViewer /></ProtectedRoute>} />
          <Route path="/settings" element={<ProtectedRoute><OrgPage /></ProtectedRoute>} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
