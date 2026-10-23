import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from '../App';

describe('App', () => {
  it('runs a simulation from the button', () => {
    render(<App />);
    fireEvent.click(screen.getByText('模拟'));
    expect(screen.getByText('总伤害')).toBeTruthy();
  });
});
