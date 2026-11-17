import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { NumbersPage } from './NumbersPage';
import { QueuePage } from './QueuePage';
import './styles.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element missing');
}

createRoot(root).render(
  <StrictMode>
    {window.location.pathname === '/numbers' ? <NumbersPage /> : <QueuePage />}
  </StrictMode>,
);
