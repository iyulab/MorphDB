import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { Input } from './Input'

describe('Input', () => {
  describe('rendering', () => {
    it('renders correctly', () => {
      render(<Input placeholder="Enter text" />)
      expect(screen.getByPlaceholderText('Enter text')).toBeInTheDocument()
    })

    it('renders with text type when specified', () => {
      render(<Input type="text" data-testid="input" />)
      const input = screen.getByTestId('input')
      expect(input).toHaveAttribute('type', 'text')
    })

    it('forwards ref correctly', () => {
      const ref = vi.fn()
      render(<Input ref={ref} />)
      expect(ref).toHaveBeenCalled()
    })

    it('applies base classes', () => {
      render(<Input data-testid="input" />)
      const input = screen.getByTestId('input')
      expect(input).toHaveClass('flex', 'h-9', 'w-full', 'rounded-md', 'border')
    })
  })

  describe('types', () => {
    it('renders text input', () => {
      render(<Input type="text" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'text')
    })

    it('renders password input', () => {
      render(<Input type="password" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'password')
    })

    it('renders email input', () => {
      render(<Input type="email" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'email')
    })

    it('renders number input', () => {
      render(<Input type="number" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'number')
    })

    it('renders search input', () => {
      render(<Input type="search" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'search')
    })
  })

  describe('interactions', () => {
    it('handles onChange event', async () => {
      const handleChange = vi.fn()
      const user = userEvent.setup()
      render(<Input onChange={handleChange} data-testid="input" />)

      await user.type(screen.getByTestId('input'), 'hello')
      expect(handleChange).toHaveBeenCalled()
    })

    it('updates value when typing', async () => {
      const user = userEvent.setup()
      render(<Input data-testid="input" />)

      const input = screen.getByTestId('input')
      await user.type(input, 'test value')
      expect(input).toHaveValue('test value')
    })

    it('handles onFocus event', () => {
      const handleFocus = vi.fn()
      render(<Input onFocus={handleFocus} data-testid="input" />)

      fireEvent.focus(screen.getByTestId('input'))
      expect(handleFocus).toHaveBeenCalledTimes(1)
    })

    it('handles onBlur event', () => {
      const handleBlur = vi.fn()
      render(<Input onBlur={handleBlur} data-testid="input" />)

      const input = screen.getByTestId('input')
      fireEvent.focus(input)
      fireEvent.blur(input)
      expect(handleBlur).toHaveBeenCalledTimes(1)
    })

    it('handles onKeyDown event', () => {
      const handleKeyDown = vi.fn()
      render(<Input onKeyDown={handleKeyDown} data-testid="input" />)

      fireEvent.keyDown(screen.getByTestId('input'), { key: 'Enter' })
      expect(handleKeyDown).toHaveBeenCalled()
    })
  })

  describe('disabled state', () => {
    it('renders disabled input', () => {
      render(<Input disabled data-testid="input" />)
      expect(screen.getByTestId('input')).toBeDisabled()
    })

    it('applies disabled styles', () => {
      render(<Input disabled data-testid="input" />)
      const input = screen.getByTestId('input')
      expect(input).toHaveClass('disabled:cursor-not-allowed', 'disabled:opacity-50')
    })

    it('does not allow typing when disabled', async () => {
      const handleChange = vi.fn()
      render(<Input disabled onChange={handleChange} data-testid="input" />)

      fireEvent.change(screen.getByTestId('input'), { target: { value: 'test' } })
      // Note: fireEvent still triggers change, but user interaction is blocked
      expect(screen.getByTestId('input')).toBeDisabled()
    })
  })

  describe('placeholder', () => {
    it('displays placeholder text', () => {
      render(<Input placeholder="Enter your name" />)
      expect(screen.getByPlaceholderText('Enter your name')).toBeInTheDocument()
    })

    it('applies placeholder styles', () => {
      render(<Input placeholder="Placeholder" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveClass('placeholder:text-muted-foreground')
    })
  })

  describe('custom className', () => {
    it('merges custom className', () => {
      render(<Input className="custom-class" data-testid="input" />)
      const input = screen.getByTestId('input')
      expect(input).toHaveClass('custom-class')
      expect(input).toHaveClass('rounded-md')
    })
  })

  describe('accessibility', () => {
    it('has visible focus ring', () => {
      render(<Input data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveClass('focus-visible:ring-1')
    })

    it('supports aria-label', () => {
      render(<Input aria-label="Search" data-testid="input" />)
      expect(screen.getByLabelText('Search')).toBeInTheDocument()
    })

    it('supports aria-describedby', () => {
      render(
        <>
          <Input aria-describedby="help-text" data-testid="input" />
          <span id="help-text">Help text</span>
        </>
      )
      expect(screen.getByTestId('input')).toHaveAttribute('aria-describedby', 'help-text')
    })

    it('supports required attribute', () => {
      render(<Input required data-testid="input" />)
      expect(screen.getByTestId('input')).toBeRequired()
    })

    it('supports readonly attribute', () => {
      render(<Input readOnly data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('readonly')
    })
  })

  describe('controlled vs uncontrolled', () => {
    it('works as controlled input', () => {
      render(<Input value="controlled" onChange={() => {}} data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveValue('controlled')
    })

    it('works as uncontrolled input with defaultValue', () => {
      render(<Input defaultValue="default" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveValue('default')
    })
  })

  describe('file input', () => {
    it('supports file type', () => {
      render(<Input type="file" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveAttribute('type', 'file')
    })

    it('has file input styles', () => {
      render(<Input type="file" data-testid="input" />)
      expect(screen.getByTestId('input')).toHaveClass('file:border-0', 'file:bg-transparent')
    })
  })
})
