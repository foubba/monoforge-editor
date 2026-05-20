"""
Sample Python file for verifying syntax highlighting in MonoForge.
"""
from typing import List


def fibonacci(n: int) -> List[int]:
    """Return the first n Fibonacci numbers."""
    if n <= 0:
        return []
    seq = [0, 1]
    while len(seq) < n:
        seq.append(seq[-1] + seq[-2])
    return seq[:n]


class Counter:
    def __init__(self, start: int = 0):
        self.value = start

    def bump(self) -> int:
        self.value += 1
        return self.value


if __name__ == "__main__":
    print("First 10 Fibonacci:", fibonacci(10))
    c = Counter()
    for _ in range(3):
        print("tick", c.bump())
