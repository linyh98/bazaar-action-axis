import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../../App';

describe('App', () => {
  it('renders the simulator title', () => {
    render(<App />);
    expect(screen.getByText('大巴扎行动轴模拟器')).toBeTruthy();
  });
});
