#!/usr/bin/env python3
"""
Analysis script for Leaders and Followers replication system.

This script measures the performance of a single-leader key-value store
with semi-synchronous replication by varying the write quorum.

Purpose:
    - Understand how write quorum affects latency in distributed systems
    - Demonstrate the trade-off between consistency and performance
    - Verify data consistency across replicas

What we learn:
    - Higher quorum = more followers must ACK = higher latency
    - The "slowest link" effect: waiting for more nodes means waiting for slower ones
    - Semi-synchronous replication provides tunable consistency guarantees
"""

import asyncio
import aiohttp
import time
import statistics
import matplotlib.pyplot as plt
from dataclasses import dataclass
from typing import List, Dict

# Configuration
LEADER_URL = "http://localhost:8080"
FOLLOWER_URLS = [
    "http://localhost:8081",
    "http://localhost:8082",
    "http://localhost:8083",
    "http://localhost:8084",
    "http://localhost:8085",
]
NUM_KEYS = 10
NUM_WRITES_PER_KEY = 10  # 10 keys * 10 writes = 100 total writes
CONCURRENT_WRITES = 10   # 10 at a time
QUORUM_VALUES = [1, 2, 3, 4, 5]


@dataclass
class LatencyStats:
    """Statistics for a set of latency measurements."""
    mean: float
    median: float
    p95: float
    p99: float
    min_val: float
    max_val: float


async def set_quorum(session: aiohttp.ClientSession, quorum: int) -> bool:
    """Set the write quorum via the /config endpoint."""
    try:
        async with session.post(
            f"{LEADER_URL}/config",
            json={"WriteQuorum": quorum, "MinDelayMs": 0, "MaxDelayMs": 1000}
        ) as resp:
            return resp.status == 200
    except Exception as e:
        print(f"Failed to set quorum: {e}")
        return False


async def write_key(session: aiohttp.ClientSession, key: str, value: str) -> float:
    """
    Write a key-value pair and return the latency in seconds.
    Returns -1 if the write failed.
    """
    start = time.perf_counter()
    try:
        async with session.post(
            f"{LEADER_URL}/set",
            params={"key": key, "value": value}
        ) as resp:
            elapsed = time.perf_counter() - start
            if resp.status == 200:
                data = await resp.json()
                if data.get("success"):
                    return elapsed
            return -1
    except Exception as e:
        print(f"Write failed for {key}: {e}")
        return -1


async def run_concurrent_writes(
    session: aiohttp.ClientSession,
    keys: List[str],
    num_writes_per_key: int,
    concurrency: int
) -> List[float]:
    """
    Run writes concurrently with a semaphore to limit parallelism.
    Returns list of successful latencies.
    """
    semaphore = asyncio.Semaphore(concurrency)
    latencies = []

    async def bounded_write(key: str, value: str):
        async with semaphore:
            return await write_key(session, key, value)

    # Create all write tasks
    tasks = []
    for i in range(num_writes_per_key):
        for key in keys:
            value = f"value_{key}_{i}_{time.time()}"
            tasks.append(bounded_write(key, value))

    # Execute all tasks
    results = await asyncio.gather(*tasks)

    # Filter successful writes
    for latency in results:
        if latency >= 0:
            latencies.append(latency)

    return latencies


def calculate_stats(latencies: List[float]) -> LatencyStats:
    """Calculate latency statistics."""
    if not latencies:
        return LatencyStats(0, 0, 0, 0, 0, 0)

    sorted_lats = sorted(latencies)
    n = len(sorted_lats)

    return LatencyStats(
        mean=statistics.mean(latencies),
        median=statistics.median(latencies),
        p95=sorted_lats[int(n * 0.95)] if n > 1 else sorted_lats[0],
        p99=sorted_lats[int(n * 0.99)] if n > 1 else sorted_lats[0],
        min_val=min(latencies),
        max_val=max(latencies)
    )


async def get_all_data(session: aiohttp.ClientSession, url: str) -> Dict:
    """Get all data from a node."""
    try:
        async with session.get(f"{url}/dump") as resp:
            if resp.status == 200:
                return await resp.json()
            return {}
    except Exception as e:
        print(f"Failed to get data from {url}: {e}")
        return {}


async def check_consistency(session: aiohttp.ClientSession) -> Dict:
    """Check if all followers have the same data as the leader."""
    leader_data = await get_all_data(session, LEADER_URL)

    results = {
        "leader_keys": len(leader_data),
        "followers": []
    }

    for i, url in enumerate(FOLLOWER_URLS, 1):
        follower_data = await get_all_data(session, url)
        
        # Count matching keys, mismatched values, and missing keys
        matching_count = 0
        mismatched_values = []
        missing_keys = []
        
        for k in leader_data.keys():
            if k not in follower_data:
                missing_keys.append(k)
            elif leader_data[k] == follower_data[k]:
                matching_count += 1
            else:
                mismatched_values.append(k)
        
        is_consistent = len(mismatched_values) == 0 and len(missing_keys) == 0
        
        results["followers"].append({
            "follower": f"f{i}",
            "keys": len(follower_data),
            "matches_leader": is_consistent,
            "matching_count": matching_count,
            "mismatched_values": len(mismatched_values),
            "missing_keys": len(missing_keys),
            "mismatched_examples": mismatched_values[:3]  # Show first 3 examples
        })

    return results


async def run_analysis():
    """Main analysis function."""
    print("=" * 60)
    print("Leaders and Followers Replication Analysis")
    print("=" * 60)

    keys = [f"key_{i}" for i in range(NUM_KEYS)]
    all_stats: Dict[int, LatencyStats] = {}

    async with aiohttp.ClientSession() as session:
        # Test each quorum value
        for quorum in QUORUM_VALUES:
            print(f"\n--- Testing Quorum = {quorum} ---")

            # Set the quorum via API
            if not await set_quorum(session, quorum):
                print(f"Failed to set quorum to {quorum}, skipping...")
                continue

            # Wait a moment for config to apply
            await asyncio.sleep(0.5)

            print(f"Running {NUM_KEYS * NUM_WRITES_PER_KEY} writes "
                  f"({CONCURRENT_WRITES} concurrent)...")

            latencies = await run_concurrent_writes(
                session, keys, NUM_WRITES_PER_KEY, CONCURRENT_WRITES
            )

            if latencies:
                stats = calculate_stats(latencies)
                all_stats[quorum] = stats

                print(f"  Successful writes: {len(latencies)}")
                print(f"  Mean latency:   {stats.mean*1000:.1f} ms")
                print(f"  Median latency: {stats.median*1000:.1f} ms")
                print(f"  P95 latency:    {stats.p95*1000:.1f} ms")
                print(f"  P99 latency:    {stats.p99*1000:.1f} ms")
            else:
                print("  No successful writes!")

        # Check consistency after all writes
        print("\n" + "=" * 60)
        print("Data Consistency Check")
        print("=" * 60)

        # Give async replication time to complete
        await asyncio.sleep(2)

        consistency = await check_consistency(session)
        print(f"Leader has {consistency['leader_keys']} keys")

        for f in consistency["followers"]:
            status = "✓ CONSISTENT" if f["matches_leader"] else "✗ INCONSISTENT"
            print(f"  {f['follower']}: {f['keys']} keys - {status}")
            print(f"         Matching: {f['matching_count']}, "
                  f"Mismatched values: {f['mismatched_values']}, "
                  f"Missing: {f['missing_keys']}")
            if f['mismatched_examples']:
                print(f"         Example mismatched keys: {f['mismatched_examples']}")

    # Plot results
    if all_stats:
        plot_results(all_stats)

    return all_stats


def plot_results(stats: Dict[int, LatencyStats]):
    """Plot quorum vs latency graph."""
    quorums = list(stats.keys())
    means = [stats[q].mean for q in quorums]
    medians = [stats[q].median for q in quorums]
    p95s = [stats[q].p95 for q in quorums]
    p99s = [stats[q].p99 for q in quorums]

    plt.figure(figsize=(10, 6))

    plt.plot(quorums, means, 'b-o', label='mean', linewidth=2)
    plt.plot(quorums, medians, 'orange', marker='s', label='median', linewidth=2)
    plt.plot(quorums, p95s, 'g-^', label='p95', linewidth=2)
    plt.plot(quorums, p99s, 'r-d', label='p99', linewidth=2)

    plt.xlabel('Quorum value')
    plt.ylabel('Latency (s)')
    plt.title('Quorum vs. Latency, random delay in range [0, 1000ms]')
    plt.xticks(quorums, [f'Q={q}' for q in quorums])
    plt.legend()
    plt.grid(True, alpha=0.3)
    plt.tight_layout()

    plt.savefig('quorum_latency_analysis.png', dpi=150)
    print(f"\nPlot saved to: quorum_latency_analysis.png")
    plt.close()  # Close without showing


def explain_metrics():
    """Print explanation of the metrics used."""
    print("""
================================================================================
                          EXPLANATION OF METRICS
================================================================================

WHAT IS THIS ANALYSIS?
----------------------
This script measures how the "write quorum" affects write latency in a 
distributed key-value store using semi-synchronous replication.

- Leader: Accepts all writes, replicates to followers
- Followers: Receive replicated data from the leader  
- Write Quorum: Number of followers that must ACK before a write is "successful"

LATENCY METRICS EXPLAINED:
--------------------------

1. MEAN (Average)
   - Sum of all latencies divided by count
   - Gives overall "typical" performance
   - Problem: Sensitive to outliers (one slow request skews the average)

2. MEDIAN (50th Percentile / P50)
   - The middle value when sorted
   - Half of requests are faster, half are slower
   - Better represents "typical" user experience than mean
   - Not affected by extreme outliers

3. P95 (95th Percentile)
   - 95% of requests are faster than this value
   - Only 5% of requests are slower
   - Shows "worst case for most users"
   - Important for SLAs (Service Level Agreements)

4. P99 (99th Percentile)  
   - 99% of requests are faster than this value
   - Only 1% of requests are slower
   - Shows "tail latency" - the slow outliers
   - Critical for understanding worst-case performance
   - At scale: 1% of millions of requests = thousands of slow requests!

WHY DO WE NEED ALL THESE?
-------------------------
Example: 100 requests with latencies in ms:
- 99 requests: 10ms each
- 1 request: 1000ms (network hiccup)

Mean = (99*10 + 1000)/100 = 19.9ms  <- Misleading! Most requests were 10ms
Median = 10ms                        <- Accurate representation
P95 = 10ms                           <- 95% of users see this
P99 = 1000ms                         <- The "unlucky" 1% see this

EXPECTED RESULTS:
-----------------
As quorum increases from 1 to 5:

1. LATENCY INCREASES because:
   - Must wait for MORE followers to acknowledge
   - With random delays [0, 1000ms], higher quorum = wait for slower nodes
   - "Slowest link" effect: Q=5 waits for the SLOWEST of 5 random delays

2. P95/P99 increase faster than mean/median because:
   - Higher quorum amplifies variance
   - More chances to hit a "slow" node
   - Tail latency grows with system complexity

3. The relationship is NOT linear because:
   - Order statistics: expected value of k-th largest of n uniform samples
   - Q=1: Wait for fastest (min) ~ 0ms-200ms typical
   - Q=5: Wait for slowest (max) ~ 800ms-1000ms typical

CONSISTENCY RESULTS:
--------------------
After writes complete, followers may have different data because:
- Semi-synchronous: Only QUORUM followers must ACK for success
- Remaining followers receive updates asynchronously
- With Q<5: Some followers may lag behind

This demonstrates the CAP theorem trade-off:
- Higher Quorum = More Consistency, Higher Latency
- Lower Quorum = Better Performance, Eventual Consistency

================================================================================
""")


if __name__ == "__main__":
    # Print explanation first
    explain_metrics()

    # Run the analysis
    print("\nStarting analysis (assuming Docker containers are running)...")
    print("Make sure to run: docker-compose up -d\n")

    try:
        asyncio.run(run_analysis())
    except KeyboardInterrupt:
        print("\nAnalysis interrupted.")
    except Exception as e:
        print(f"\nError: {e}")
        print("Make sure Docker containers are running: docker-compose up -d")
