/** Decorative product artwork: never blocks opening the app or announces duplicate copy. */
export function Mascot({ className = '', eager = false }: { className?: string; eager?: boolean }) {
  return <img className={`wallet-mascot ${className}`} src="/images/wallet-buddy.webp" alt="" aria-hidden="true"
    width="512" height="512" decoding="async" loading={eager ? 'eager' : 'lazy'} fetchPriority="low" />
}
