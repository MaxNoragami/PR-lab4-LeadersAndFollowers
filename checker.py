#!/usr/bin/env python3
import requests
import time
import statistics
import json
import os
import concurrent.futures
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
NUM_WRITES_PER_KEY = 10
CONCURRENT_WRITES = 10 
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


def set_quorum(quorum: int) -> bool:
    """Set the write quorum via the /config endpoint."""
    try:
        response = requests.post(
            f"{LEADER_URL}/config",
            json={"writeQuorum": quorum},
            timeout=5
        )
        return response.status_code == 200
    except Exception as e:
        print(f"Failed to set quorum: {e}")
        return False


def write_key(key: str, value: str) -> float:
    """
    Write a key-value pair and return the latency in seconds.
    Returns -1 if the write failed.
    """
    start = time.perf_counter()
    try:
        response = requests.post(
            f"{LEADER_URL}/set",
            params={"key": key, "value": value},
            timeout=10
        )
        elapsed = time.perf_counter() - start
        if response.status_code == 200:
            data = response.json()
            if data.get("success"):
                return elapsed
        return -1
    except Exception as e:
        print(f"Write failed for {key}: {e}")
        return -1


def run_concurrent_writes(
    keys: List[str],
    num_writes_per_key: int,
    concurrency: int
) -> List[float]:
    """
    Run writes concurrently with a thread pool.
    Returns list of successful latencies.
    """
    # Create all write tasks
    tasks = []
    for i in range(num_writes_per_key):
        for key in keys:
            value = f"value_{key}_{i}_{time.time()}"
            tasks.append((key, value))

    latencies = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as executor:
        futures = [executor.submit(write_key, key, value) for key, value in tasks]
        for future in concurrent.futures.as_completed(futures):
            latency = future.result()
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


def get_all_data(url: str) -> Dict:
    """Get all data from a node."""
    try:
        response = requests.get(f"{url}/dump", timeout=5)
        if response.status_code == 200:
            return response.json()
        return {}
    except Exception as e:
        print(f"Failed to get data from {url}: {e}")
        return {}


def get_all_versions(url: str) -> Dict[str, int]:
    """Get all key:version pairs from a node."""
    try:
        response = requests.get(f"{url}/dump-versions", timeout=5)
        if response.status_code == 200:
            return response.json()
        return {}
    except Exception as e:
        print(f"Failed to get versions from {url}: {e}")
        return {}


def save_stores(quorum: int, keys: List[str], results_dir: str = "results"):
    """Save key:version data for leader and all followers to JSON files."""
    # Create quorum-specific folder
    quorum_dir = os.path.join(results_dir, f"q{quorum}")
    os.makedirs(quorum_dir, exist_ok=True)
    
    # Get and save leader data
    leader_versions = get_all_versions(LEADER_URL)
    # Filter to only requested keys and sort alphabetically
    leader_filtered = {k: leader_versions[k] for k in sorted(keys) if k in leader_versions}
    
    with open(os.path.join(quorum_dir, "leader_store.json"), "w") as f:
        json.dump(leader_filtered, f, indent=2)
    
    # Get and save follower data
    for i, url in enumerate(FOLLOWER_URLS, 1):
        follower_versions = get_all_versions(url)
        # Filter to only requested keys and sort alphabetically
        follower_filtered = {k: follower_versions[k] for k in sorted(keys) if k in follower_versions}
        
        with open(os.path.join(quorum_dir, f"f{i}_store.json"), "w") as f:
            json.dump(follower_filtered, f, indent=2)
    
    print(f"  Saved stores to {quorum_dir}/")


def check_consistency(keys: List[str] = None) -> Dict:
    """Check if all followers have the same data as the leader for specified keys."""
    leader_data = get_all_data(LEADER_URL)
    
    # If specific keys provided, filter to only those
    if keys:
        leader_data = {k: v for k, v in leader_data.items() if k in keys}

    results = {
        "leader_keys": len(leader_data),
        "followers": []
    }

    for i, url in enumerate(FOLLOWER_URLS, 1):
        follower_data = get_all_data(url)
        
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
            "mismatched_examples": mismatched_values[:3]
        })

    return results


def run_analysis():
    """Main analysis function."""
    print("=" * 60)
    print("Leaders and Followers Replication Analysis")
    print("=" * 60)

    all_stats: Dict[int, LatencyStats] = {}
    all_consistency: Dict[int, Dict] = {}
    
    # Create results directory
    results_dir = "results"
    os.makedirs(results_dir, exist_ok=True)

    # Test each quorum value
    for quorum in QUORUM_VALUES:
        print(f"\n--- Testing Quorum = {quorum} ---")

        # Set the quorum via API
        if not set_quorum(quorum):
            print(f"Failed to set quorum to {quorum}, skipping...")
            continue

        # Wait a moment for config to apply
        time.sleep(0.5)

        # Use simple keys: key_0 to key_9
        keys = [f"key_{i}" for i in range(NUM_KEYS)]

        print(f"Running {NUM_KEYS * NUM_WRITES_PER_KEY} writes "
              f"({CONCURRENT_WRITES} concurrent)...")

        latencies = run_concurrent_writes(keys, NUM_WRITES_PER_KEY, CONCURRENT_WRITES)

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

        # Check consistency after this quorum's writes
        print(f"\n  Consistency check for Q={quorum}:")
        time.sleep(1)  # Give async replication time to complete
        
        consistency = check_consistency(keys)
        all_consistency[quorum] = consistency
        
        # Save stores to JSON files
        save_stores(quorum, keys, results_dir)
        
        all_match = all(f["matches_leader"] for f in consistency["followers"])
        
        # Calculate consistency percentage
        total_keys_checked = consistency['leader_keys'] * len(consistency["followers"])
        total_matching = sum(f["matching_count"] for f in consistency["followers"])
        consistency_pct = (total_matching / total_keys_checked * 100) if total_keys_checked > 0 else 0
        consistency["consistency_pct"] = consistency_pct
        
        if all_match:
            print(f"  ✓ All followers consistent ({consistency['leader_keys']} keys) - {consistency_pct:.1f}%")
        else:
            print(f"  Leader has {consistency['leader_keys']} keys for this quorum - {consistency_pct:.1f}% consistent")
            for f in consistency["followers"]:
                status = "✓" if f["matches_leader"] else "✗"
                print(f"    {f['follower']}: {status} Matching: {f['matching_count']}, "
                      f"Mismatched: {f['mismatched_values']}, Missing: {f['missing_keys']}")

    # Summary
    print("\n" + "=" * 60)
    print("Consistency Summary by Quorum")
    print("=" * 60)
    for quorum, consistency in all_consistency.items():
        all_match = all(f["matches_leader"] for f in consistency["followers"])
        total_mismatched = sum(f["mismatched_values"] for f in consistency["followers"])
        total_missing = sum(f["missing_keys"] for f in consistency["followers"])
        pct = consistency.get("consistency_pct", 0)
        status = f"✓ 100%" if all_match else f"✗ {pct:.1f}% ({total_mismatched} mismatched, {total_missing} missing)"
        print(f"  Q={quorum}: {status}")

    # Plot results
    if all_stats:
        plot_results(all_stats, results_dir)
    
    if all_consistency:
        plot_consistency(all_consistency, results_dir)

    return all_stats


def plot_results(stats: Dict[int, LatencyStats], results_dir: str = "results"):
    """Plot quorum vs latency graph."""
    quorums = list(stats.keys())
    means = [stats[q].mean for q in quorums]
    medians = [stats[q].median for q in quorums]
    p95s = [stats[q].p95 for q in quorums]
    p99s = [stats[q].p99 for q in quorums]

    plt.figure(figsize=(10, 6))

    plt.plot(quorums, means, color='#779ECB', marker='o', label='mean', linewidth=2)
    plt.plot(quorums, medians, color='#FFB347', marker='s', label='median', linewidth=2)
    plt.plot(quorums, p95s, color='#77DD77', marker='^', label='p95', linewidth=2)
    plt.plot(quorums, p99s, color='#FF6961', marker='d', label='p99', linewidth=2)

    plt.xlabel('Quorum value')
    plt.ylabel('Latency (s)')
    plt.title('Quorum vs. Latency, random delay in range [0, 1000ms]')
    plt.xticks(quorums, [f'Q={q}' for q in quorums])
    plt.legend()
    plt.grid(True, alpha=0.8, which='both', linestyle='-', linewidth=0.5)
    plt.minorticks_on()
    plt.grid(True, alpha=0.8, which='minor', linestyle=':', linewidth=0.5)
    plt.tight_layout()

    plot_path = os.path.join(results_dir, 'quorum_latency_analysis.png')
    plt.savefig(plot_path, dpi=150)
    print(f"\nPlot saved to: {plot_path}")
    plt.close()


def plot_consistency(all_consistency: Dict[int, Dict], results_dir: str = "results"):
    """Plot quorum vs consistency percentage graph."""
    quorums = list(all_consistency.keys())
    percentages = [all_consistency[q].get("consistency_pct", 0) for q in quorums]

    plt.figure(figsize=(10, 6))

    plt.plot(quorums, percentages, color='#B39EB5', marker='o', linewidth=2, markersize=8)
    
    for q, pct in zip(quorums, percentages):
        plt.text(q, pct + 1.5, f'{pct:.1f}%', ha='center', va='bottom', fontsize=10, fontweight='bold')

    plt.xlabel('Quorum value')
    plt.ylabel('Consistency (%)')
    plt.title('Quorum vs. Data Consistency Across Followers')
    plt.xticks(quorums, [f'Q={q}' for q in quorums])
    plt.ylim(min(percentages) - 5 if percentages else 0, 105)  # Dynamic lower bound
    plt.grid(True, alpha=0.8, which='both', linestyle='-', linewidth=0.5)
    plt.minorticks_on()
    plt.grid(True, alpha=0.8, which='minor', linestyle=':', linewidth=0.5)
    plt.tight_layout()

    plot_path = os.path.join(results_dir, 'quorum_consistency_analysis.png')
    plt.savefig(plot_path, dpi=150)
    print(f"Plot saved to: {plot_path}")
    plt.close()


if __name__ == "__main__":
    print("\nStarting analysis (assuming Docker containers are running)...")
    print("Make sure to run: docker-compose up -d\n")

    try:
        run_analysis()
    except KeyboardInterrupt:
        print("\nAnalysis interrupted.")
    except Exception as e:
        print(f"\nError: {e}")
        print("Make sure Docker containers are running: docker-compose up -d")
